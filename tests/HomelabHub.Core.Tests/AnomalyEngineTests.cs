using HomelabHub.Abstractions.Events;
using HomelabHub.Core.Anomalies;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HomelabHub.Core.Tests;

/// <summary>
/// La machine à états d'ADR-0005 : transformer une répétition en un petit nombre de transitions.
/// </summary>
/// <remarks>
/// Le cas qui a motivé ces tests est réel : un import bloqué republie sa clé à chaque cycle
/// depuis dix heures. Il doit produire <b>une</b> notification, pas six cents — puis <b>une</b>
/// de plus quand il se résout.
/// </remarks>
public sealed class AnomalyEngineTests
{
    private const string Module = "media";
    private const string Key = "media.import.pending:aa";

    private static readonly DateTimeOffset Start = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);

    private static AnomalyEngine NewEngine(IAnomalyStore? store = null) =>
        new(store ?? new RecordingAnomalyStore(), NullLogger<AnomalyEngine>.Instance);

    private static HubEvent Event(HubEventSeverity severity = HubEventSeverity.Warning,
                                  string title = "Import en attente") =>
        new(Module, "media.import.pending", severity, title, "Manual Import required.",
            Key, null, Start);

    /// <summary>Un cycle complet : ouverture, observations, fermeture.</summary>
    private static IReadOnlyList<AnomalyTransition> Cycle(
        AnomalyEngine engine, DateTimeOffset now, params HubEvent[] observed)
    {
        engine.BeginCycle(Module);
        foreach (var e in observed)
        {
            engine.Observe(Module, e);
        }

        return engine.CompleteCycle(Module, succeeded: true, now);
    }

    // ── Ouverture et répétition ──────────────────────────────────────────────────────

    [Fact]
    public void La_premiere_observation_ouvre_lanomalie()
    {
        var engine = NewEngine();

        var transition = Assert.Single(Cycle(engine, Start, Event()));

        Assert.Equal(AnomalyTransitionKind.Opened, transition.Kind);
        Assert.Equal(AnomalyState.Open, transition.Anomaly.State);
        Assert.Equal(1, transition.Anomaly.Occurrences);
    }

    [Fact]
    public void Republier_la_meme_cle_ne_produit_plus_aucune_transition()
    {
        // Le cœur du problème : sans cela, dix heures de blocage donneraient six cents
        // notifications à un intervalle de soixante secondes.
        var engine = NewEngine();
        Cycle(engine, Start, Event());

        for (var cycle = 1; cycle <= 600; cycle++)
        {
            var transitions = Cycle(engine, Start.AddMinutes(cycle), Event());
            Assert.Empty(transitions);
        }

        var anomaly = Assert.Single(engine.Active);
        Assert.Equal(601, anomaly.Occurrences);
        Assert.Equal(TimeSpan.FromMinutes(600), anomaly.Duration);
    }

    // ── Fermeture, le chemin symétrique ──────────────────────────────────────────────

    [Fact]
    public void Cesser_de_republier_resout_lanomalie_au_cycle_suivant()
    {
        // Le chemin de sortie, symétrique de l'ouverture : dix cycles qui observent, puis un
        // cycle qui n'observe plus. L'anomalie doit se fermer immédiatement, sans que personne
        // n'ait à la fermer explicitement.
        var engine = NewEngine();

        for (var cycle = 0; cycle < 10; cycle++)
        {
            Cycle(engine, Start.AddMinutes(cycle), Event());
        }

        Assert.Single(engine.Active);

        var transition = Assert.Single(Cycle(engine, Start.AddMinutes(10)));

        Assert.Equal(AnomalyTransitionKind.Resolved, transition.Kind);
        Assert.Equal(AnomalyState.Resolved, transition.Anomaly.State);
        Assert.Equal(Start.AddMinutes(10), transition.Anomaly.ResolvedAt);
        Assert.Empty(engine.Active);
    }

    [Fact]
    public void Une_anomalie_resolue_puis_reapparue_ouvre_une_nouvelle_occurrence()
    {
        // Le compteur repart de zéro : sinon l'historique décrirait un épisode qui n'existe plus.
        var engine = NewEngine();
        Cycle(engine, Start, Event());
        Cycle(engine, Start.AddMinutes(1));

        var transition = Assert.Single(Cycle(engine, Start.AddMinutes(2), Event()));

        Assert.Equal(AnomalyTransitionKind.Opened, transition.Kind);
        Assert.Equal(1, transition.Anomaly.Occurrences);
        Assert.Equal(Start.AddMinutes(2), transition.Anomaly.OpenedAt);
    }

    // ── Cycle en échec : l'absence n'est pas significative ───────────────────────────

    [Fact]
    public void Un_cycle_en_echec_ne_resout_rien()
    {
        // Sans cette garde, un service injoignable produirait une salve de « tout va bien » au
        // moment précis où quelque chose ne va pas (ADR-0005).
        var engine = NewEngine();
        Cycle(engine, Start, Event());

        engine.BeginCycle(Module);
        var transitions = engine.CompleteCycle(Module, succeeded: false, Start.AddMinutes(1));

        Assert.Empty(transitions);
        Assert.Single(engine.Active);
    }

    [Fact]
    public void Un_module_ne_resout_pas_les_anomalies_dun_autre()
    {
        var engine = NewEngine();
        Cycle(engine, Start, Event());

        engine.BeginCycle("system");
        var transitions = engine.CompleteCycle("system", succeeded: true, Start.AddMinutes(1));

        Assert.Empty(transitions);
        Assert.Single(engine.Active);
    }

    // ── Aggravation ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Une_aggravation_produit_une_transition_mais_pas_une_reouverture()
    {
        var engine = NewEngine();
        Cycle(engine, Start, Event(HubEventSeverity.Warning));

        var transition = Assert.Single(Cycle(engine, Start.AddMinutes(1), Event(HubEventSeverity.Critical)));

        Assert.Equal(AnomalyTransitionKind.Escalated, transition.Kind);
        Assert.Equal(HubEventSeverity.Critical, transition.Anomaly.Severity);
        Assert.Equal(Start, transition.Anomaly.OpenedAt);
    }

    [Fact]
    public void Une_amelioration_de_gravite_ne_produit_pas_de_transition()
    {
        var engine = NewEngine();
        Cycle(engine, Start, Event(HubEventSeverity.Critical));

        Assert.Empty(Cycle(engine, Start.AddMinutes(1), Event(HubEventSeverity.Warning)));
    }

    // ── Mise en sommeil ─────────────────────────────────────────────────────────────

    [Fact]
    public void Une_anomalie_en_sommeil_sort_des_actives_sans_etre_resolue()
    {
        var engine = NewEngine();
        Cycle(engine, Start, Event());

        Assert.True(engine.Snooze(Key, Start.AddHours(6), Start));

        Assert.Empty(engine.Active);
        Assert.Equal(AnomalyState.Snoozed, Assert.Single(engine.All).State);
    }

    [Fact]
    public void Le_sommeil_expire_et_reveille_lanomalie_si_elle_dure_toujours()
    {
        var engine = NewEngine();
        Cycle(engine, Start, Event());
        engine.Snooze(Key, Start.AddHours(6), Start);

        Assert.Empty(Cycle(engine, Start.AddHours(3), Event()));

        var transition = Assert.Single(Cycle(engine, Start.AddHours(6), Event()));

        Assert.Equal(AnomalyTransitionKind.Reopened, transition.Kind);
        Assert.Equal(AnomalyState.Open, transition.Anomaly.State);
    }

    [Fact]
    public void Un_sommeil_jusqua_resolution_ne_se_reveille_jamais_de_lui_meme()
    {
        // « Ignorer jusqu'à résolution » : le réarmement n'a lieu qu'après un passage effectif
        // par l'état résolu, pas à une échéance.
        var engine = NewEngine();
        Cycle(engine, Start, Event());
        engine.Snooze(Key, until: null, Start);

        for (var day = 1; day <= 30; day++)
        {
            Assert.Empty(Cycle(engine, Start.AddDays(day), Event()));
        }

        Assert.Empty(engine.Active);
    }

    [Fact]
    public void Apres_resolution_un_sommeil_jusqua_resolution_est_rearme()
    {
        var engine = NewEngine();
        Cycle(engine, Start, Event());
        engine.Snooze(Key, until: null, Start);

        // Elle disparaît : la mise en sommeil n'empêche pas la résolution.
        var resolved = Assert.Single(Cycle(engine, Start.AddMinutes(1)));
        Assert.Equal(AnomalyTransitionKind.Resolved, resolved.Kind);

        // Elle revient : nouvelle occurrence, et le sommeil ne s'applique plus.
        var reopened = Assert.Single(Cycle(engine, Start.AddMinutes(2), Event()));
        Assert.Equal(AnomalyTransitionKind.Opened, reopened.Kind);
        Assert.Single(engine.Active);
    }

    [Fact]
    public void On_ne_met_pas_en_sommeil_une_anomalie_resolue_ou_inconnue()
    {
        var engine = NewEngine();

        Assert.False(engine.Snooze("inconnue", null, Start));

        Cycle(engine, Start, Event());
        Cycle(engine, Start.AddMinutes(1));

        Assert.False(engine.Snooze(Key, null, Start.AddMinutes(2)));
    }

    // ── Observations hors cycle ─────────────────────────────────────────────────────

    [Fact]
    public void Une_observation_hors_cycle_est_ignoree_pour_la_reconciliation()
    {
        // Sans borne, on ne peut pas savoir ce qui aurait dû être republié : l'observation
        // reste au journal mais n'entre pas dans la table.
        var engine = NewEngine();

        engine.Observe(Module, Event());

        Assert.Empty(engine.All);
    }

    [Fact]
    public void Un_evenement_sans_cle_de_deduplication_nest_pas_une_anomalie()
    {
        var engine = NewEngine();

        engine.BeginCycle(Module);
        engine.Observe(Module, new HubEvent(Module, "media.info", HubEventSeverity.Info,
                                            "Fait ponctuel", null, null, null, Start));
        var transitions = engine.CompleteCycle(Module, succeeded: true, Start);

        Assert.Empty(transitions);
        Assert.Empty(engine.All);
    }
}
