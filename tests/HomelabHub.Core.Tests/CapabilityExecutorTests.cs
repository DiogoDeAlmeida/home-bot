using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Core.Capabilities;
using HomelabHub.Core.Modules;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HomelabHub.Core.Tests;

/// <summary>
/// L'autorisation est décidée ici et nulle part ailleurs (ADR-0004). Ces tests sont la seule
/// preuve que les boutons Discord — qui n'ont aucune permission native — sont protégés.
/// </summary>
public sealed class CapabilityExecutorTests
{
    private const string QueryKey = "system.status";
    private const string MutationKey = "system.restart";
    private const string RestOnlyKey = "system.backup.create";

    private static CapabilityExecutor NewExecutor(bool moduleActive = true) =>
        new(new FakeRegistry(), new FakeModules(moduleActive), NullLogger<CapabilityExecutor>.Instance);

    private static CapabilityInvocation Invoke(string key, InvocationSource source, bool admin,
                                               Dictionary<string, object?>? arguments = null) =>
        new(key, arguments ?? [], source, "test", admin);

    [Fact]
    public async Task Une_mutation_appelee_par_un_non_administrateur_est_refusee()
    {
        var result = await NewExecutor().ExecuteAsync(
            Invoke(MutationKey, InvocationSource.DiscordCommand, admin: false), TestContext.Current.CancellationToken);

        Assert.Equal(CapabilityOutcome.Failed, result.Outcome);
        Assert.Contains("administrateurs", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Une_mutation_declenchee_par_un_bouton_Discord_est_soumise_au_meme_controle()
    {
        // Discord n'offre aucune permission sur les composants de message : sans cette
        // vérification, n'importe quel membre voyant le dashboard pourrait cliquer.
        var result = await NewExecutor().ExecuteAsync(
            Invoke(MutationKey, InvocationSource.DiscordComponent, admin: false), TestContext.Current.CancellationToken);

        Assert.Equal(CapabilityOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task Une_mutation_appelee_par_un_administrateur_passe()
    {
        var result = await NewExecutor().ExecuteAsync(
            Invoke(MutationKey, InvocationSource.DiscordCommand, admin: true), TestContext.Current.CancellationToken);

        Assert.Equal(CapabilityOutcome.Ok, result.Outcome);
    }

    [Fact]
    public async Task Une_capacite_restreinte_au_REST_refuse_un_appel_Discord()
    {
        var result = await NewExecutor().ExecuteAsync(
            Invoke(RestOnlyKey, InvocationSource.DiscordCommand, admin: true), TestContext.Current.CancellationToken);

        Assert.Equal(CapabilityOutcome.Failed, result.Outcome);
        Assert.Contains("cette interface", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task La_meme_capacite_passe_depuis_le_REST()
    {
        var result = await NewExecutor().ExecuteAsync(
            Invoke(RestOnlyKey, InvocationSource.Rest, admin: true), TestContext.Current.CancellationToken);

        Assert.Equal(CapabilityOutcome.Ok, result.Outcome);
    }

    [Fact]
    public async Task Un_module_inactif_refuse_ses_capacites()
    {
        var result = await NewExecutor(moduleActive: false).ExecuteAsync(
            Invoke(QueryKey, InvocationSource.Rest, admin: true), TestContext.Current.CancellationToken);

        Assert.Equal(CapabilityOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task Une_capacite_inconnue_echoue_proprement()
    {
        var result = await NewExecutor().ExecuteAsync(
            Invoke("media.queue.list", InvocationSource.Rest, admin: true), TestContext.Current.CancellationToken);

        Assert.Equal(CapabilityOutcome.Failed, result.Outcome);
        Assert.Contains("inconnue", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Une_exception_dans_la_capacite_ne_remonte_pas_a_lappelant()
    {
        // Convention §14 : une capacité qui explose dégrade la réponse, elle ne fait pas
        // tomber le hub et n'expose pas de pile d'appels.
        var result = await NewExecutor().ExecuteAsync(
            Invoke("system.boom", InvocationSource.Rest, admin: true), TestContext.Current.CancellationToken);

        Assert.Equal(CapabilityOutcome.Failed, result.Outcome);
        Assert.DoesNotContain("Exception", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_parametre_obligatoire_manquant_est_refuse_avant_execution()
    {
        var result = await NewExecutor().ExecuteAsync(
            Invoke("system.echo", InvocationSource.Rest, admin: true), TestContext.Current.CancellationToken);

        Assert.Equal(CapabilityOutcome.Failed, result.Outcome);
        Assert.Contains("obligatoire", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_argument_non_declare_est_ignore()
    {
        var result = await NewExecutor().ExecuteAsync(
            Invoke("system.echo", InvocationSource.Rest, admin: true, new Dictionary<string, object?>
            {
                ["texte"] = "bonjour",
                ["injecté"] = "ne doit pas passer",
            }), TestContext.Current.CancellationToken);

        var arguments = Assert.IsType<Dictionary<string, object?>>(result.Payload);
        Assert.Equal("bonjour", arguments["texte"]);
        Assert.DoesNotContain("injecté", arguments.Keys);
    }

    // ── Doublures ────────────────────────────────────────────────────────────────────

    private sealed class FakeRegistry : ICapabilityRegistry
    {
        public IReadOnlyList<RegisteredCapability> All { get; } =
        [
            Wrap(new StubCapability(QueryKey, CapabilityKind.Query, CapabilityExposure.All)),
            Wrap(new StubCapability(MutationKey, CapabilityKind.Mutation, CapabilityExposure.All)),
            Wrap(new StubCapability(RestOnlyKey, CapabilityKind.Mutation, CapabilityExposure.Rest)),
            Wrap(new ThrowingCapability()),
            Wrap(new EchoCapability()),
        ];

        public RegisteredCapability? Find(string capabilityKey) =>
            All.FirstOrDefault(c => c.Descriptor.Key == capabilityKey);

        public IReadOnlyList<RegisteredCapability> ForModule(string moduleKey, CapabilityExposure exposure) =>
            [.. All.Where(c => c.Descriptor.Exposure.HasFlag(exposure))];

        private static RegisteredCapability Wrap(IHubCapability capability) =>
            new("system", capability.Descriptor, capability);
    }

    private sealed class FakeModules(bool active) : IModuleRegistry
    {
        public IReadOnlyList<ModuleDescriptor> Modules => [];

        public bool IsActive(string moduleKey) => active;

        public ModuleActivation GetActivation(string moduleKey) =>
            new(moduleKey, active, active, active ? null : "Module désactivé pour le test.");

        public Task SetEnabledAsync(string moduleKey, bool enabled, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class StubCapability(string key, CapabilityKind kind, CapabilityExposure exposure)
        : IHubCapability
    {
        public CapabilityDescriptor Descriptor { get; } =
            new(key, key, "Doublure de test.", [], kind, exposure);

        public Task<CapabilityResult> ExecuteAsync(CapabilityInvocation invocation,
                                                   CancellationToken cancellationToken) =>
            Task.FromResult(CapabilityResult.Ok());
    }

    private sealed class ThrowingCapability : IHubCapability
    {
        public CapabilityDescriptor Descriptor { get; } =
            new("system.boom", "Boum", "Lève systématiquement.", [], CapabilityKind.Query);

        public Task<CapabilityResult> ExecuteAsync(CapabilityInvocation invocation,
                                                   CancellationToken cancellationToken) =>
            throw new InvalidOperationException("panne simulée");
    }

    private sealed class EchoCapability : IHubCapability
    {
        public CapabilityDescriptor Descriptor { get; } = new(
            "system.echo", "Écho", "Renvoie ses arguments liés.",
            [new CapabilityParameter("texte", "Texte", CapabilityParameterType.String, Required: true)],
            CapabilityKind.Query);

        public Task<CapabilityResult> ExecuteAsync(CapabilityInvocation invocation,
                                                   CancellationToken cancellationToken) =>
            Task.FromResult(CapabilityResult.Ok(invocation.Arguments));
    }
}
