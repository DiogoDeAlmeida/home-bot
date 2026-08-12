using HomelabHub.Abstractions.Events;
using HomelabHub.Core.Anomalies;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HomelabHub.Core.Tests;

/// <summary>
/// Ce que la persistance achète : un redémarrage qui ne renotifie plus (ADR-0017).
/// </summary>
/// <remarks>
/// Chaque test rejoue la même chose — un moteur, un magasin, puis un <b>second</b> moteur monté
/// sur le même magasin. C'est la seule façon d'éprouver un redémarrage sans en faire un.
/// </remarks>
public sealed class AnomalyPersistenceTests
{
    private const string Module = "media";
    private const string Key = "media.import.pending:aa";

    private static readonly DateTimeOffset Start = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);

    private static AnomalyEngine Engine(IAnomalyStore store) =>
        new(store, NullLogger<AnomalyEngine>.Instance);

    private static HubEvent Event(string moduleKey = Module, string key = Key) =>
        new(moduleKey, "media.import.pending", HubEventSeverity.Warning,
            "Import en attente", "Manual Import required.", key, null, Start);

    private static IReadOnlyList<AnomalyTransition> Cycle(
        AnomalyEngine engine, string moduleKey, DateTimeOffset now, params HubEvent[] observed)
    {
        engine.BeginCycle(moduleKey);
        foreach (var e in observed)
        {
            engine.Observe(moduleKey, e);
        }

        return engine.CompleteCycle(moduleKey, succeeded: true, now);
    }

    // ── Le bénéfice attendu ──────────────────────────────────────────────────────────

    [Fact]
    public void Un_redemarrage_ne_renotifie_pas_une_anomalie_toujours_presente()
    {
        // Le défaut que la tranche corrige. Avant la base, chaque redémarrage repartait d'une
        // table vide : l'import bloqué depuis dix heures rouvrait, et notifiait à nouveau.
        var store = new RecordingAnomalyStore();

        var avant = Engine(store);
        Cycle(avant, Module, Start, Event());

        var apres = Engine(store);
        apres.Hydrate([Module], Start.AddMinutes(5));

        var transitions = Cycle(apres, Module, Start.AddMinutes(6), Event());

        Assert.Empty(transitions);
    }

    [Fact]
    public void Un_redemarrage_conserve_lheure_douverture_et_le_compteur()
    {
        // « Bloqué depuis dix heures » n'est redemandable à aucun service : c'est précisément
        // ce que le hub possède, et ce qu'il perdrait sans la table.
        var store = new RecordingAnomalyStore();

        var avant = Engine(store);
        Cycle(avant, Module, Start, Event());
        Cycle(avant, Module, Start.AddHours(5), Event());
        Cycle(avant, Module, Start.AddHours(10), Event());

        var apres = Engine(store);
        apres.Hydrate([Module], Start.AddHours(10).AddMinutes(1));

        var anomaly = Assert.Single(apres.Active);
        Assert.Equal(Start, anomaly.OpenedAt);
        Assert.Equal(3, anomaly.Occurrences);
        Assert.Equal(TimeSpan.FromHours(10), anomaly.Duration);
    }

    [Fact]
    public void Une_anomalie_resolue_avant_larret_ne_revient_pas_ouverte()
    {
        var store = new RecordingAnomalyStore();

        var avant = Engine(store);
        Cycle(avant, Module, Start, Event());
        Cycle(avant, Module, Start.AddMinutes(1));   // plus republiée : résolue

        var apres = Engine(store);
        apres.Hydrate([Module], Start.AddMinutes(2));

        Assert.Empty(apres.Active);
        Assert.Equal(AnomalyState.Resolved, Assert.Single(apres.All).State);
    }

    [Fact]
    public void Une_mise_en_sommeil_survit_au_redemarrage()
    {
        // Sinon le sommeil serait annulé par le premier redémarrage, soit exactement le bruit
        // qu'il sert à éteindre.
        var store = new RecordingAnomalyStore();

        var avant = Engine(store);
        Cycle(avant, Module, Start, Event());
        Assert.True(avant.Snooze(Key, Start.AddHours(6), Start));

        var apres = Engine(store);
        apres.Hydrate([Module], Start.AddMinutes(1));

        var anomaly = Assert.Single(apres.All);
        Assert.Equal(AnomalyState.Snoozed, anomaly.State);
        Assert.Equal(Start.AddHours(6), anomaly.SnoozedUntil);
        Assert.Empty(apres.Active);
    }

    [Fact]
    public void Le_sommeil_recharge_expire_toujours_a_lheure_dite()
    {
        // La date d'échéance doit survivre au redémarrage, pas seulement l'état « en sommeil » :
        // un réveil qui ne se déclenche jamais est pire qu'une absence de sommeil.
        var store = new RecordingAnomalyStore();

        var avant = Engine(store);
        Cycle(avant, Module, Start, Event());
        avant.Snooze(Key, Start.AddHours(6), Start);

        var apres = Engine(store);
        apres.Hydrate([Module], Start.AddHours(6).AddMinutes(1));

        var transition = Assert.Single(Cycle(apres, Module, Start.AddHours(6).AddMinutes(2), Event()));
        Assert.Equal(AnomalyTransitionKind.Reopened, transition.Kind);
    }

    // ── Le piège que la persistance introduit ────────────────────────────────────────

    [Fact]
    public void Une_anomalie_dun_module_disparu_est_close_au_demarrage()
    {
        // Sans cette réconciliation, une anomalie dont le module a été retiré du binaire ou a
        // changé de clé resterait ouverte pour toujours : plus personne ne la republie, donc
        // plus personne ne peut la résoudre. C'est un défaut que la table en mémoire n'avait
        // pas, puisqu'elle repartait de zéro.
        var store = new RecordingAnomalyStore();

        var avant = Engine(store);
        Cycle(avant, "ancien", Start, Event(moduleKey: "ancien", key: "ancien.truc:1"));
        Assert.Single(avant.Active);

        var apres = Engine(store);
        apres.Hydrate(["media", "system"], Start.AddMinutes(1));

        Assert.Empty(apres.Active);

        var anomaly = Assert.Single(apres.All);
        Assert.Equal(AnomalyState.Resolved, anomaly.State);
        Assert.Equal(Start.AddMinutes(1), anomaly.ResolvedAt);
    }

    [Fact]
    public void La_fermeture_doffice_dune_orpheline_nemet_aucune_transition()
    {
        // Une mise à jour qui retire un module ne doit pas produire une salve de « résolu » dans
        // Discord : personne n'a rien réparé.
        var store = new RecordingAnomalyStore();

        var avant = Engine(store);
        Cycle(avant, "ancien", Start, Event(moduleKey: "ancien", key: "ancien.truc:1"));

        var apres = Engine(store);
        apres.Hydrate(["media"], Start.AddMinutes(1));

        // Hydrate ne retourne rien par construction ; ce que l'on vérifie est qu'aucun cycle
        // ultérieur ne rattrape la transition manquée.
        Assert.Empty(Cycle(apres, "media", Start.AddMinutes(2)));
    }

    [Fact]
    public void Un_module_present_mais_inactif_garde_ses_anomalies()
    {
        // Distinction volontaire : désactiver le module média depuis l'interface n'efface pas ce
        // qu'il avait signalé. Le réactiver reprend exactement où il en était.
        var store = new RecordingAnomalyStore();

        var avant = Engine(store);
        Cycle(avant, Module, Start, Event());

        var apres = Engine(store);
        apres.Hydrate([Module], Start.AddMinutes(1));   // au catalogue, mais aucun cycle ne tourne

        Assert.Single(apres.Active);
    }

    // ── Rétention ────────────────────────────────────────────────────────────────────

    [Fact]
    public void La_purge_efface_les_resolues_anciennes_et_epargne_les_ouvertes()
    {
        var store = new RecordingAnomalyStore();
        var engine = Engine(store);

        Cycle(engine, Module, Start, Event(), Event(key: "media.download.stalled:bb"));
        Cycle(engine, Module, Start.AddMinutes(1), Event());   // « stalled » n'est plus republiée

        var removed = engine.PurgeResolved(Start.AddDays(15));

        Assert.Equal(1, removed);
        Assert.Single(engine.All);
        Assert.Equal(Key, engine.All[0].DedupeKey);
        Assert.Single(store.Load());
    }

    [Fact]
    public void La_purge_ninvente_pas_de_disparition_cote_interface()
    {
        // La table en mémoire doit être alignée sur la base : sinon l'interface continuerait
        // d'afficher un historique que la base ne porte plus, et le redémarrage suivant ferait
        // disparaître des lignes sans explication.
        var store = new RecordingAnomalyStore();
        var engine = Engine(store);

        Cycle(engine, Module, Start, Event());
        Cycle(engine, Module, Start.AddMinutes(1));

        engine.PurgeResolved(Start.AddDays(15));

        var apres = Engine(store);
        apres.Hydrate([Module], Start.AddDays(15));

        Assert.Equal(engine.All.Count, apres.All.Count);
    }

    // ── Écriture ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Un_cycle_sans_anomalie_nappelle_pas_le_magasin()
    {
        // Le cas nominal, des milliers de fois par jour : rien ne va mal, donc rien ne s'écrit.
        var store = new RecordingAnomalyStore();
        var engine = Engine(store);

        Cycle(engine, Module, Start);
        Cycle(engine, Module, Start.AddMinutes(1));

        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public void Un_cycle_en_echec_necrit_rien()
    {
        // Symétrique de la règle d'ADR-0005 : un cycle abandonné ne réconcilie rien, donc il n'a
        // rien à persister non plus.
        var store = new RecordingAnomalyStore();
        var engine = Engine(store);

        Cycle(engine, Module, Start, Event());
        var writesApresOuverture = store.Writes;

        engine.BeginCycle(Module);
        engine.CompleteCycle(Module, succeeded: false, Start.AddMinutes(1));

        Assert.Equal(writesApresOuverture, store.Writes);
        Assert.Single(engine.Active);
    }

    [Fact]
    public void Une_base_en_panne_narrete_pas_la_surveillance()
    {
        // C'est quand la machine va mal qu'on a besoin qu'elle continue de regarder. Le hub
        // dégrade — il oublie l'heure d'ouverture — mais il ne s'arrête pas.
        var engine = Engine(new FailingAnomalyStore());

        var transition = Assert.Single(Cycle(engine, Module, Start, Event()));

        Assert.Equal(AnomalyTransitionKind.Opened, transition.Kind);
        Assert.Single(engine.Active);
    }

    private sealed class FailingAnomalyStore : IAnomalyStore
    {
        public IReadOnlyList<Anomaly> Load() => [];

        public void Save(IReadOnlyList<Anomaly> anomalies) => throw new IOException("disque plein");

        public int PurgeResolvedBefore(DateTimeOffset cutoff) => 0;
    }
}
