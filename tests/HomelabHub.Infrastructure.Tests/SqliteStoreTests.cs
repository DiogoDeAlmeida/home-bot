using HomelabHub.Abstractions.Events;
using HomelabHub.Core.Anomalies;
using Xunit;

namespace HomelabHub.Infrastructure.Tests;

/// <summary>
/// Les magasins SQLite, éprouvés contre un vrai fichier.
/// </summary>
/// <remarks>
/// Les tests du noyau vérifient la machine à états avec un magasin en mémoire ; ceux-ci
/// vérifient que ce que le noyau croit écrire arrive réellement sur le disque et en revient
/// identique. Les deux moitiés du même contrat.
/// </remarks>
public sealed class SqliteStoreTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);

    private static Anomaly Anomaly(string key = "media.import.pending:aa",
                                   AnomalyState state = AnomalyState.Open,
                                   DateTimeOffset? resolvedAt = null) =>
        new(DedupeKey: key,
            ModuleKey: "media",
            Type: "media.import.pending",
            Severity: HubEventSeverity.Warning,
            Title: "Import en attente",
            Body: "Manual Import required.",
            Data: new Dictionary<string, string> { ["downloadId"] = "481b6e36", ["éclairé"] = "oui" },
            State: state,
            OpenedAt: Start,
            LastSeenAt: Start.AddHours(1),
            ResolvedAt: resolvedAt,
            SnoozedUntil: null,
            Occurrences: 42);

    // ── Anomalies ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Une_anomalie_ecrite_revient_identique()
    {
        using var hub = new TemporaryHub(withDatabase: true);
        var store = hub.Anomalies!;

        var written = Anomaly();
        store.Save([written]);

        var read = Assert.Single(store.Load());

        // Le record se compare champ à champ, sauf Data : deux dictionnaires distincts ne sont
        // jamais égaux par référence. On compare donc le reste avec Data neutralisée, puis Data
        // par son contenu.
        Assert.Equal(written with { Data = null }, read with { Data = null });
        Assert.Equal(written.Data, read.Data);
    }

    [Fact]
    public void Un_horodatage_traverse_la_base_sans_perdre_son_instant()
    {
        // SQLite n'a pas de type date : tout passe par une conversion. Une dérive ferait mentir
        // « ouverte depuis dix heures », qui est la seule chose que le hub possède en propre.
        //
        // Le décalage d'origine, lui, n'est pas conservé — la colonne porte des ticks UTC, ce
        // qui est ce qui rend les comparaisons de la rétention traduisibles en SQL. L'instant
        // est identique, sa notation ne l'est pas, et c'est le compromis assumé.
        using var hub = new TemporaryHub(withDatabase: true);
        var store = hub.Anomalies!;

        var opened = new DateTimeOffset(2026, 8, 12, 10, 30, 15, 123, TimeSpan.FromHours(2));
        store.Save([Anomaly() with { OpenedAt = opened }]);

        var read = Assert.Single(store.Load());

        Assert.Equal(opened.UtcTicks, read.OpenedAt.UtcTicks);
        Assert.Equal(TimeSpan.Zero, read.OpenedAt.Offset);
    }

    [Fact]
    public void Reecrire_la_meme_cle_met_a_jour_sans_dupliquer()
    {
        // La clé de déduplication est la clé primaire : le doublon est impossible par
        // construction, et c'est exactement ce qu'on veut vérifier une fois.
        using var hub = new TemporaryHub(withDatabase: true);
        var store = hub.Anomalies!;

        store.Save([Anomaly()]);
        store.Save([Anomaly() with { Occurrences = 43, LastSeenAt = Start.AddHours(2) }]);

        var read = Assert.Single(store.Load());

        Assert.Equal(43, read.Occurrences);
        Assert.Equal(Start.AddHours(2), read.LastSeenAt);
    }

    [Fact]
    public void La_purge_efface_les_resolues_anciennes_et_epargne_les_ouvertes()
    {
        using var hub = new TemporaryHub(withDatabase: true);
        var store = hub.Anomalies!;

        store.Save(
        [
            Anomaly(key: "ouverte"),
            Anomaly(key: "recente", state: AnomalyState.Resolved, resolvedAt: Start.AddDays(-1)),
            Anomaly(key: "ancienne", state: AnomalyState.Resolved, resolvedAt: Start.AddDays(-30)),
        ]);

        var removed = store.PurgeResolvedBefore(Start.AddDays(-14));

        Assert.Equal(1, removed);
        Assert.Equal(["ouverte", "recente"], store.Load().Select(a => a.DedupeKey).Order());
    }

    // ── Journal ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Le_journal_rend_les_evenements_du_plus_recent_au_plus_ancien()
    {
        using var hub = new TemporaryHub(withDatabase: true);
        var store = hub.Journal!;

        for (var i = 0; i < 5; i++)
        {
            store.Append(Event($"n{i}", Start.AddMinutes(i)));
        }

        var recent = store.Recent(3, null);

        Assert.Equal(["n4", "n3", "n2"], recent.Select(e => e.Title));
    }

    [Fact]
    public void Deux_evenements_du_meme_instant_gardent_leur_ordre_dinsertion()
    {
        // Les événements d'un même cycle portent souvent le même horodatage à la milliseconde
        // près. Trier sur la date les rendrait dans un ordre arbitraire, qui change d'un appel à
        // l'autre — un journal qui se réordonne tout seul est illisible.
        using var hub = new TemporaryHub(withDatabase: true);
        var store = hub.Journal!;

        store.Append(Event("premier", Start));
        store.Append(Event("second", Start));
        store.Append(Event("troisième", Start));

        Assert.Equal(["troisième", "second", "premier"], store.Recent(10, null).Select(e => e.Title));
    }

    [Fact]
    public void Le_journal_filtre_par_gravite_minimale()
    {
        using var hub = new TemporaryHub(withDatabase: true);
        var store = hub.Journal!;

        store.Append(Event("info", Start, HubEventSeverity.Info));
        store.Append(Event("avertissement", Start, HubEventSeverity.Warning));
        store.Append(Event("critique", Start, HubEventSeverity.Critical));

        var filtered = store.Recent(10, HubEventSeverity.Warning);

        Assert.Equal(["critique", "avertissement"], filtered.Select(e => e.Title));
    }

    [Fact]
    public void La_retention_par_age_supprime_ce_qui_depasse_la_fenetre()
    {
        using var hub = new TemporaryHub(withDatabase: true);
        var store = hub.Journal!;

        store.Append(Event("vieux", Start.AddDays(-20)));
        store.Append(Event("récent", Start.AddDays(-2)));

        var removed = store.Purge(Start.AddDays(-14), maximumRows: 100_000);

        Assert.Equal(1, removed);
        Assert.Equal("récent", Assert.Single(store.Recent(10, null)).Title);
    }

    [Fact]
    public void La_retention_par_nombre_ne_garde_que_les_dernieres_lignes()
    {
        // La seconde borne du cadrage : 14 jours OU 100 000 lignes, la première atteinte
        // l'emportant. Un module bavard peut saturer la fenêtre d'âge en deux jours.
        using var hub = new TemporaryHub(withDatabase: true);
        var store = hub.Journal!;

        for (var i = 0; i < 50; i++)
        {
            store.Append(Event($"n{i}", Start));
        }

        var removed = store.Purge(Start.AddDays(-14), maximumRows: 10);

        Assert.Equal(40, removed);

        var kept = store.Recent(100, null);
        Assert.Equal(10, kept.Count);
        Assert.Equal("n49", kept[0].Title);
        Assert.Equal("n40", kept[^1].Title);
    }

    [Fact]
    public void Un_evenement_sans_donnees_ni_cle_traverse_la_base_sans_dommage()
    {
        // Le cas nominal d'un fait ponctuel : ni DedupeKey, ni Data, ni Body.
        using var hub = new TemporaryHub(withDatabase: true);
        var store = hub.Journal!;

        var fact = new HubEvent("system", "system.started", HubEventSeverity.Info,
                                "Démarrage", null, null, null, Start);

        store.Append(fact);

        var read = Assert.Single(store.Recent(10, null));

        Assert.Equal(fact, read);
        Assert.Null(read.Data);
        Assert.Null(read.DedupeKey);
    }

    private static HubEvent Event(string title, DateTimeOffset at,
                                  HubEventSeverity severity = HubEventSeverity.Info) =>
        new("media", "media.download.progress", severity, title, "détail", null,
            new Dictionary<string, string> { ["clé"] = "valeur" }, at);
}
