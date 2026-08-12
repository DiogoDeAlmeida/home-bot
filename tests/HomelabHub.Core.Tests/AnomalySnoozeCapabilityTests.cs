using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Abstractions.Events;
using HomelabHub.Core.Anomalies;
using HomelabHub.Core.Capabilities;
using HomelabHub.Core.Configuration;
using HomelabHub.Core.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HomelabHub.Core.Tests;

/// <summary>
/// <c>hub.anomaly.snooze</c> : la première capacité portée par le noyau plutôt que par un
/// module.
/// </summary>
/// <remarks>
/// Elle existe pour une raison précise : la mise en sommeil n'appartient à aucun domaine, et la
/// faire passer par <see cref="ICapabilityExecutor"/> plutôt que par un appel direct à
/// <see cref="AnomalyEngine.Snooze"/> lui donne gratuitement l'autorisation, le journal d'audit
/// et l'exposition REST — au lieu d'une seconde voie d'exécution pour une mutation.
/// </remarks>
public sealed class AnomalySnoozeCapabilityTests
{
    private const string Module = "media";
    private const string Key = "media.import.pending:aa";

    private static readonly DateTimeOffset Start = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);

    private static AnomalyEngine Engine() => new(new RecordingAnomalyStore(), NullLogger<AnomalyEngine>.Instance);

    private static void Open(AnomalyEngine engine)
    {
        engine.BeginCycle(Module);
        engine.Observe(Module, new HubEvent(Module, "media.import.pending", HubEventSeverity.Warning,
            "Import en attente", "Manual Import required.", Key, null, Start));
        engine.CompleteCycle(Module, succeeded: true, Start);
    }

    private static CapabilityInvocation Invoke(string dedupeKey, long? hours) =>
        new("hub.anomaly.snooze",
            hours is { } h
                ? new Dictionary<string, object?> { ["key"] = dedupeKey, ["hours"] = h }
                : new Dictionary<string, object?> { ["key"] = dedupeKey },
            InvocationSource.ChatButton, "discord:test", IsAdministrator: true);

    // ── La capacité elle-même ───────────────────────────────────────────────────────

    [Fact]
    public void La_capacite_sinscrit_sous_le_prefixe_reserve_du_noyau()
    {
        var descriptor = new AnomalySnoozeCapability(Engine(), TimeProvider.System).Descriptor;

        Assert.Equal("hub.anomaly.snooze", descriptor.Key);
        Assert.Equal(CapabilityKind.Mutation, descriptor.Kind);
        Assert.Equal(["anomaly", "snooze"], descriptor.Command!.Path);

        // Contrairement à l'import manuel ou à l'écriture qBittorrent : aucun service externe
        // n'est touché, seul un bouton à un clic était visé au cadrage.
        Assert.False(descriptor.RequireConfirmation);
    }

    [Fact]
    public async Task Snoozer_une_anomalie_ouverte_reussit()
    {
        var engine = Engine();
        Open(engine);

        var result = await new AnomalySnoozeCapability(engine, TimeProvider.System)
            .ExecuteAsync(Invoke(Key, hours: 6), TestContext.Current.CancellationToken);

        Assert.Equal(CapabilityOutcome.Ok, result.Outcome);
        Assert.Equal(AnomalyState.Snoozed, Assert.Single(engine.All).State);
    }

    [Fact]
    public async Task Sans_duree_lanomalie_est_tue_jusqua_resolution()
    {
        var engine = Engine();
        Open(engine);

        await new AnomalySnoozeCapability(engine, TimeProvider.System)
            .ExecuteAsync(Invoke(Key, hours: null), TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(engine.All).SnoozedUntil);
    }

    [Fact]
    public async Task Une_anomalie_inconnue_echoue_proprement()
    {
        var result = await new AnomalySnoozeCapability(Engine(), TimeProvider.System)
            .ExecuteAsync(Invoke("inconnue", hours: null), TestContext.Current.CancellationToken);

        Assert.Equal(CapabilityOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task Une_cle_absente_echoue_sans_toucher_le_moteur()
    {
        var invocation = new CapabilityInvocation("hub.anomaly.snooze", new Dictionary<string, object?>(),
            InvocationSource.Api, "web:admin", IsAdministrator: true);

        var result = await new AnomalySnoozeCapability(Engine(), TimeProvider.System)
            .ExecuteAsync(invocation, TestContext.Current.CancellationToken);

        Assert.Equal(CapabilityOutcome.Failed, result.Outcome);
    }

    // ── Câblage complet : catalogue, registre, exécuteur ─────────────────────────────

    [Fact]
    public void Le_registre_lenregistre_sous_la_cle_hub_comme_un_pseudo_module()
    {
        using var services = BuildContainer();
        var registry = services.GetRequiredService<ICapabilityRegistry>();

        var registered = registry.Find("hub.anomaly.snooze");

        Assert.NotNull(registered);
        Assert.Equal(HubSettings.Prefix, registered.ModuleKey);
    }

    [Fact]
    public async Task Executee_via_lexecuteur_elle_traverse_la_meme_autorisation_que_les_autres_mutations()
    {
        // La preuve qui compte : un appelant non administrateur se fait refuser exactement comme
        // pour n'importe quelle Mutation de module — parce que c'est le même code qui décide.
        using var services = BuildContainer();
        var executor = services.GetRequiredService<ICapabilityExecutor>();

        var refused = await executor.ExecuteAsync(
            Invoke(Key, hours: 6) with { IsAdministrator = false },
            TestContext.Current.CancellationToken);

        Assert.Equal(CapabilityOutcome.Failed, refused.Outcome);
        Assert.Contains("administrateurs", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Executee_via_lexecuteur_ne_consulte_jamais_lactivation_dun_module()
    {
        // Le pseudo-module « hub » n'a pas de bascule d'activation : ModuleRegistry.GetActivation
        // lèverait sur cette clé (ModuleCatalog.Get), puisqu'aucun module ne peut la revendiquer.
        // Ce test échouerait bruyamment si CapabilityExecutor recommençait à l'interroger.
        using var services = BuildContainer();
        var engine = services.GetRequiredService<AnomalyEngine>();
        Open(engine);

        var executor = services.GetRequiredService<ICapabilityExecutor>();
        var result = await executor.ExecuteAsync(Invoke(Key, hours: 6), TestContext.Current.CancellationToken);

        Assert.Equal(CapabilityOutcome.Ok, result.Outcome);
    }

    private static ServiceProvider BuildContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAnomalyStore, RecordingAnomalyStore>();
        services.AddSingleton<AnomalyEngine>();
        services.AddSingleton<IAnomalyEngine>(sp => sp.GetRequiredService<AnomalyEngine>());
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<AnomalySnoozeCapability>();
        services.AddSingleton(new HubCapabilityCatalog([typeof(AnomalySnoozeCapability)]));

        // Aucun module réel n'est nécessaire pour ce test : le catalogue de modules reste vide,
        // et c'est précisément ce qui prouve que la capacité du noyau n'en dépend pas.
        services.AddSingleton(new ModuleCatalog([]));
        services.AddSingleton<IModuleRegistry, ThrowingModuleRegistry>();

        services.AddSingleton<ICapabilityRegistry, CapabilityRegistry>();
        services.AddSingleton<ICapabilityExecutor, CapabilityExecutor>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Lève sur tout appel : si <see cref="CapabilityExecutor"/> consultait encore l'activation
    /// pour le pseudo-module « hub », ce test le ferait échouer au lieu de le laisser passer en
    /// silence.
    /// </summary>
    private sealed class ThrowingModuleRegistry : IModuleRegistry
    {
        public IReadOnlyList<ModuleDescriptor> Modules => [];

        public bool IsActive(string moduleKey) => throw new InvalidOperationException(
            $"IsActive ne devrait jamais être appelé pour « {moduleKey} ».");

        public ModuleActivation GetActivation(string moduleKey) => throw new InvalidOperationException(
            $"GetActivation ne devrait jamais être appelé pour « {moduleKey} ».");

        public Task SetEnabledAsync(string moduleKey, bool enabled, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("SetEnabledAsync ne devrait jamais être appelé.");
    }
}
