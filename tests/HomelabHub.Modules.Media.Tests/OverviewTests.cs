using HomelabHub.Modules.Media.Contracts;
using HomelabHub.Modules.Media.Correlation;
using Xunit;

namespace HomelabHub.Modules.Media.Tests;

/// <summary>
/// Le classement et le résumé, qui garantissent que tous les adaptateurs montrent la même chose.
/// </summary>
/// <remarks>
/// Si le tri vivait dans les adaptateurs, le message d'un salon et la page web afficheraient des
/// sélections différentes du même instant — divergence invisible tant qu'on ne les compare pas.
/// Ces tests figent le critère à un seul endroit.
/// </remarks>
public sealed class OverviewTests
{
    private static MediaJourney Journey(
        string key,
        double progress,
        bool attention = false,
        JourneyState state = JourneyState.Downloading)
    {
        // Une progression donnée se construit par les octets, puisque c'est ainsi qu'elle est
        // calculée : mille octets au total, dont on laisse la fraction manquante.
        var download = new DownloadItem(
            DownloadId: key,
            Title: key,
            Size: 1000,
            SizeLeft: (long)(1000 * (1 - progress)),
            State: DownloadState.Downloading,
            Health: attention ? DownloadHealth.Warning : DownloadHealth.Ok,
            Torrent: null,
            Episodes: [],
            AddedAt: DateTimeOffset.UtcNow,
            Terminal: null,
            StatusMessages: []);

        return new MediaJourney(key, MediaKind.Movie, key, null, null, null, [download], state);
    }

    private static MediaSnapshot Snapshot(params MediaJourney[] journeys) =>
        new(journeys, [], DateTimeOffset.UtcNow);

    [Fact]
    public void Ce_qui_va_mal_passe_avant_ce_qui_est_presque_fini()
    {
        // Le critère convenu : un téléchargement bloqué mérite d'être vu avant un téléchargement
        // sain à 97 %. Le premier demande une décision, le second de la patience.
        var snapshot = Snapshot(
            Journey("sain-97", 0.97),
            Journey("bloque-03", 0.03, attention: true));

        var top = snapshot.MostInteresting(5);

        Assert.Equal("bloque-03", top[0].Key);
        Assert.Equal("sain-97", top[1].Key);
    }

    [Fact]
    public void A_egalite_dattention_le_plus_proche_de_la_fin_passe_devant()
    {
        var snapshot = Snapshot(
            Journey("a", 0.10),
            Journey("b", 0.90),
            Journey("c", 0.50));

        var keys = snapshot.MostInteresting(5).Select(j => j.Key).ToArray();

        Assert.Equal(["b", "c", "a"], keys);
    }

    [Fact]
    public void Le_palmares_est_borne_a_cinq()
    {
        var snapshot = Snapshot([.. Enumerable.Range(0, 12).Select(i => Journey($"j{i:d2}", i / 12d))]);

        var overview = MediaOverview.From(snapshot);

        Assert.Equal(MediaOverview.TopCount, overview.Top.Count);
        Assert.Equal(12, overview.TotalJourneys);
    }

    [Fact]
    public void Un_media_deja_disponible_nencombre_pas_le_palmares()
    {
        // Régression constatée en conditions réelles : sur 49 parcours dont un seul actif, le
        // palmarès affichait cinq médias à 100 % et masquait le téléchargement en cours. Un
        // parcours disponible a par construction une progression de 1,0, donc il gagne au tri.
        var journeys = Enumerable.Range(0, 40)
            .Select(i => Journey($"fini{i:d2}", 1d, state: JourneyState.Available))
            .Append(Journey("en-cours", 0.12))
            .ToArray();

        var top = Snapshot(journeys).MostInteresting(5);

        var only = Assert.Single(top);
        Assert.Equal("en-cours", only.Key);
    }

    [Fact]
    public void Un_media_disponible_mais_en_anomalie_reste_visible()
    {
        // L'exclusion porte sur le désintérêt, pas sur l'état : un parcours terminé dont
        // l'import a échoué doit rester en tête.
        var top = Snapshot(
            Journey("fini-ok", 1d, state: JourneyState.Available),
            Journey("fini-casse", 1d, state: JourneyState.Failed)).MostInteresting(5);

        var only = Assert.Single(top);
        Assert.Equal("fini-casse", only.Key);
    }

    [Fact]
    public void Un_import_imminent_passe_devant_un_telechargement_plus_avance()
    {
        // « Le plus proche d'aboutir » : un média en cours d'import est à quelques secondes de
        // sa disponibilité, un téléchargement à 99 % ne l'est pas encore.
        var top = Snapshot(
            Journey("presque", 0.99),
            Journey("import", 0.50, state: JourneyState.Importing)).MostInteresting(5);

        Assert.Equal("import", top[0].Key);
        Assert.Equal("presque", top[1].Key);
    }

    [Fact]
    public void Un_parcours_en_echec_ou_indetermine_demande_attention()
    {
        Assert.True(Journey("echec", 0.5, state: JourneyState.Failed).NeedsAttention);
        Assert.True(Journey("indetermine", 0.5, state: JourneyState.Unresolved).NeedsAttention);
        Assert.False(Journey("sain", 0.5).NeedsAttention);
    }

    [Fact]
    public void Le_resume_compte_les_parcours_pas_les_entrees_de_file()
    {
        // 44 entrées de file, 2 téléchargements, 1 parcours. Le résumé doit dire « 1 ».
        var snapshot = MediaCorrelator.Correlate(new CorrelationInput(
            [], Fixture.Queue("Lifecycle/12-sonarr-queue-deux-packs-downloading.json"),
            [], [], Fixture.Torrents("Lifecycle/21-qbittorrent-downloading.json"), [],
            DateTimeOffset.UtcNow, []));

        var overview = MediaOverview.From(snapshot);

        Assert.Equal(1, overview.TotalJourneys);
        Assert.Equal(1, overview.Downloading);
        Assert.Equal(20_501_032_114, overview.BytesTotal);
        Assert.Equal(2, overview.Top[0].DownloadCount);
        Assert.Equal(44, overview.Top[0].EpisodeCount);
    }

    [Fact]
    public void La_duree_restante_est_la_plus_longue_pas_la_plus_courte()
    {
        // Un média n'est disponible que quand son dernier téléchargement est fini. Prendre le
        // minimum annoncerait une disponibilité qui n'arrivera pas.
        var journey = new MediaJourney("m", MediaKind.Series, "m", null, null, null,
        [
            Download("aa", eta: 60),
            Download("bb", eta: 900),
        ], JourneyState.Downloading);

        var summary = JourneySummary.From(journey);

        Assert.Equal(TimeSpan.FromMinutes(15), summary.EstimatedTimeLeft);
    }

    [Fact]
    public void Le_resume_porte_lidentifiant_de_chaque_telechargement()
    {
        // Bug trouvé en conditions réelles : /media queue affichait titre et progression, mais
        // aucun identifiant à réutiliser — /media pause et /media resume restaient inatteignables
        // faute de savoir quoi leur donner. DownloadIds est ce qui manquait.
        var journey = new MediaJourney("m", MediaKind.Series, "m", null, null, null,
        [
            Download("AABB", eta: 60),
            Download("CCDD", eta: 900),
        ], JourneyState.Downloading);

        var summary = JourneySummary.From(journey);

        // JoinKey, pas DownloadId : la forme normalisée que les capacités comparent.
        Assert.Equal(["aabb", "ccdd"], summary.DownloadIds);
    }

    [Fact]
    public void Les_sources_injoignables_sont_exposees_au_lieu_detre_tues()
    {
        // Une liste vide parce qu'un service est éteint ne doit pas se lire « rien ne
        // télécharge » : c'est le mensonge que ServiceResult existe pour éviter.
        var snapshot = new MediaSnapshot([], ["Radarr : injoignable"], DateTimeOffset.UtcNow);

        var overview = MediaOverview.From(snapshot);

        Assert.Empty(overview.Top);
        Assert.Single(overview.UnavailableSources);
    }

    private static DownloadItem Download(string id, long eta) => new(
        DownloadId: id,
        Title: id,
        Size: 1000,
        SizeLeft: 500,
        State: DownloadState.Downloading,
        Health: DownloadHealth.Ok,
        Torrent: new QBittorrentTorrent { Hash = id, Eta = eta },
        Episodes: [],
        AddedAt: null,
        Terminal: null,
            StatusMessages: []);
}
