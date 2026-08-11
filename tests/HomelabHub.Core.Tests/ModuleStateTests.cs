using HomelabHub.Core.Modules;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HomelabHub.Core.Tests;

/// <summary>Vérifie le contrat de concurrence d'ADR-0009.</summary>
public sealed class ModuleStateTests
{
    private sealed record Counter(int Value);

    private static ModuleState<Counter> NewState(int initial = 0) =>
        new(new Counter(initial), NullLogger<ModuleState<Counter>>.Instance);

    [Fact]
    public void Mutate_publie_le_nouveau_snapshot_et_notifie()
    {
        var state = NewState();
        Counter? observed = null;
        using var subscription = state.Subscribe(snapshot => observed = snapshot);

        var result = state.Mutate(current => current with { Value = 42 });

        Assert.Equal(42, result.Value);
        Assert.Equal(42, state.Current.Value);
        Assert.Equal(42, observed?.Value);
    }

    [Fact]
    public void Renvoyer_linstance_recue_ne_notifie_pas()
    {
        // C'est le moyen explicite de dire « rien de neuf ». Sans lui, le message de dashboard
        // Discord serait réédité à chaque cycle même quand rien n'a bougé.
        var state = NewState(7);
        var notifications = 0;
        using var subscription = state.Subscribe(_ => notifications++);

        var result = state.Mutate(current => current);

        Assert.Equal(7, result.Value);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public async Task Mutations_concurrentes_ne_se_perdent_pas()
    {
        // Le cas réel : poller, webhook et connexion écrivent le même snapshot. Une simple
        // lecture-modification-écriture perdrait des incréments ; l'échange atomique avec
        // réessai n'en perd aucun.
        const int writers = 8;
        const int perWriter = 2_000;

        var state = NewState();

        await Task.WhenAll(Enumerable.Range(0, writers).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < perWriter; i++)
            {
                state.Mutate(current => current with { Value = current.Value + 1 });
            }
        })));

        Assert.Equal(writers * perWriter, state.Current.Value);
    }

    [Fact]
    public void Un_abonne_defaillant_nempeche_pas_les_autres()
    {
        var state = NewState();
        var reached = false;

        using var faulty = state.Subscribe(_ => throw new InvalidOperationException("abonné cassé"));
        using var healthy = state.Subscribe(_ => reached = true);

        state.Mutate(current => current with { Value = 1 });

        Assert.True(reached);
        Assert.Equal(1, state.Current.Value);
    }

    [Fact]
    public void Le_desabonnement_arrete_les_notifications()
    {
        var state = NewState();
        var notifications = 0;

        var subscription = state.Subscribe(_ => notifications++);
        state.Mutate(current => current with { Value = 1 });
        subscription.Dispose();
        state.Mutate(current => current with { Value = 2 });

        Assert.Equal(1, notifications);
    }
}
