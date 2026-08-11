using HomelabHub.Modules.Media.Contracts;
using HomelabHub.Modules.Media.Correlation;
using Xunit;

namespace HomelabHub.Modules.Media.Tests;

/// <summary>
/// Les cinq cas qui cassent le modèle naïf « une ligne = un média ».
/// </summary>
/// <remarks>
/// <para>
/// Écrits avant le code de corrélation, ils en ont déterminé la forme — notamment le troisième
/// niveau de multiplicité, découvert en capturant un pack de saison réel (ADR-0015).
/// </para>
/// <para>
/// Deux d'entre eux — épisodes séparés, release remplacée — n'ont pas été observés en capture.
/// Leurs données sont <b>dérivées des enregistrements réels</b> plutôt qu'inventées : on repart
/// des vraies entrées de file et on redistribue leurs <c>downloadId</c>. La forme reste celle
/// que les services produisent ; seule la topologie change.
/// </para>
/// </remarks>
public sealed class CorrelationCases
{
    private const string SonarrPacks = "Lifecycle/12-sonarr-queue-deux-packs-downloading.json";
    private const string SonarrMixed = "Lifecycle/13-sonarr-queue-downloading-et-importpending.json";
    private const string RadarrDownloading = "Lifecycle/02-radarr-queue-downloading.json";
    private const string Torrents = "Lifecycle/21-qbittorrent-downloading.json";
    private const string Requests = "Lifecycle/31-seerr-requetes-avec-downloadstatus.json";

    private static CorrelationInput Input(
        IReadOnlyList<ArrQueueRecord>? radarr = null,
        IReadOnlyList<ArrQueueRecord>? sonarr = null,
        IReadOnlyList<ArrHistoryRecord>? history = null,
        IReadOnlyList<QBittorrentTorrent>? torrents = null,
        IReadOnlyList<SeerrRequest>? requests = null) =>
        new(radarr ?? [], sonarr ?? [], history ?? [], [], torrents ?? [], requests ?? [],
            DateTimeOffset.UtcNow, []);

    // ── 1. Pack de saison : un seul téléchargement ───────────────────────────────────

    [Fact]
    public void Requete_de_saison_resolue_en_pack_unique_donne_un_seul_telechargement()
    {
        // 44 entrées de file, 2 torrents. Le modèle naïf en compterait 44.
        var snapshot = MediaCorrelator.Correlate(Input(
            sonarr: Fixture.Queue(SonarrPacks),
            torrents: Fixture.Torrents(Torrents)));

        var journey = Assert.Single(snapshot.Journeys);
        Assert.Equal(MediaKind.Series, journey.MediaType);
        Assert.Equal(2, journey.Downloads.Count);

        // Chaque téléchargement couvre 22 épisodes et compte sa taille UNE fois.
        Assert.All(journey.Downloads, download =>
        {
            Assert.Equal(22, download.Episodes.Count);
            Assert.NotNull(download.Torrent);
        });

        Assert.Equal(20_501_032_114, journey.Downloads.Sum(d => d.Size));
    }

    [Fact]
    public void La_taille_agregee_ne_compte_pas_la_meme_release_vingt_deux_fois()
    {
        // Le piège en une assertion : c'est 451 Go si l'on somme les entrées de file.
        var snapshot = MediaCorrelator.Correlate(Input(
            sonarr: Fixture.Queue(SonarrPacks),
            torrents: Fixture.Torrents(Torrents)));

        Assert.Equal(20_501_032_114, snapshot.BytesTotal);
        Assert.NotEqual(451_022_706_508, snapshot.BytesTotal);
        Assert.InRange(snapshot.Journeys[0].Progress, 0d, 1d);
    }

    // ── 2. Pack éclaté en épisodes séparés ───────────────────────────────────────────

    [Fact]
    public void Requete_de_saison_resolue_en_episodes_separes_donne_N_telechargements_agreges()
    {
        // Topologie inverse du pack : un torrent par épisode. On repart des enregistrements
        // réels et on donne à chacun son propre downloadId, avec sa part de la taille.
        var source = Fixture.Queue(SonarrPacks).Take(4).ToList();
        var separated = source.Select((record, index) => record with
        {
            DownloadId = $"{index + 1:x2}".PadLeft(2, '0').PadRight(40, 'f').ToUpperInvariant(),
            Size = 1_000_000_000,
            SizeLeft = 250_000_000,
        }).ToList();

        var snapshot = MediaCorrelator.Correlate(Input(sonarr: separated));

        var journey = Assert.Single(snapshot.Journeys);
        Assert.Equal(4, journey.Downloads.Count);
        Assert.All(journey.Downloads, d => Assert.Single(d.Episodes));

        // L'agrégation se fait sur les octets, pas sur une moyenne de pourcentages.
        Assert.Equal(4_000_000_000, journey.Downloads.Sum(d => d.Size));
        Assert.Equal(0.75d, journey.Progress, 3);
    }

    // ── 3. Import manuel, sans requête amont ─────────────────────────────────────────

    [Fact]
    public void Import_manuel_sans_requete_Seerr_produit_un_parcours_sans_demandeur()
    {
        // Aucune requête Seerr : le parcours doit exister quand même, sans demandeur, et sans
        // que l'absence soit traitée comme une anomalie.
        var snapshot = MediaCorrelator.Correlate(Input(
            radarr: Fixture.Queue(RadarrDownloading),
            torrents: Fixture.Torrents(Torrents)));

        var journey = Assert.Single(snapshot.Journeys);
        Assert.Null(journey.Request);
        Assert.Equal(MediaKind.Movie, journey.MediaType);
        Assert.Equal(JourneyState.Downloading, journey.State);
        Assert.Single(journey.Downloads);
    }

    // ── 4. Média déjà présent, jamais téléchargé ─────────────────────────────────────

    [Fact]
    public void Media_deja_present_donne_un_parcours_disponible_sans_telechargement()
    {
        // Seerr marque le média disponible ; aucune file ne le mentionne. Le parcours doit être
        // « disponible », sans téléchargement fantôme ni anomalie.
        var available = Fixture.Requests(Requests)
            .Where(r => r.Media?.Status == 5 && r.Media.ExternalServiceId is not null)
            .Take(3)
            .ToList();

        Assert.NotEmpty(available);

        var snapshot = MediaCorrelator.Correlate(Input(requests: available));

        Assert.All(snapshot.Journeys, journey =>
        {
            Assert.Empty(journey.Downloads);
            Assert.Equal(JourneyState.Available, journey.State);
            Assert.NotNull(journey.Request);
            Assert.Equal(1d, journey.Progress);
        });
    }

    // ── 5. Release remplacée par une meilleure ───────────────────────────────────────

    [Fact]
    public void Release_remplacee_ferme_lanomalie_de_lancien_telechargement()
    {
        // L'ancien downloadId a disparu de la file ; l'historique dit qu'il a échoué. Un
        // nouveau le remplace, en cours. Le parcours doit survivre au remplacement et ne pas
        // laisser l'ancien téléchargement dans un état indéterminé.
        var ancien = "aa".PadRight(40, 'a').ToUpperInvariant();
        var nouveau = Fixture.Queue(RadarrDownloading)[0].DownloadId!;

        var snapshot = MediaCorrelator.Correlate(Input(
            radarr: Fixture.Queue(RadarrDownloading),
            history:
            [
                new ArrHistoryRecord
                {
                    DownloadId = ancien, EventType = "grabbed",
                    Date = DateTimeOffset.UtcNow.AddHours(-2), MovieId = 49,
                },
                new ArrHistoryRecord
                {
                    DownloadId = ancien, EventType = "downloadFailed",
                    Date = DateTimeOffset.UtcNow.AddHours(-1), MovieId = 49,
                },
            ],
            torrents: Fixture.Torrents(Torrents)));

        var journey = Assert.Single(snapshot.Journeys);

        // Seul le téléchargement courant est présent : l'ancien n'est plus dans la file, donc
        // plus dans le parcours. Rien ne reste ouvert sur lui.
        var download = Assert.Single(journey.Downloads);
        Assert.Equal(nouveau.ToLowerInvariant(), download.DownloadId);
        Assert.Null(download.Terminal);
        Assert.Equal(JourneyState.Downloading, journey.State);
    }

    [Fact]
    public void Un_reessai_importe_lemporte_sur_un_echec_anterieur()
    {
        // Même downloadId, deux événements : échec puis import. Le tri chronologique doit faire
        // gagner le dernier, sinon une release réessayée resterait marquée en échec.
        var records = Fixture.Queue(RadarrDownloading);
        var downloadId = records[0].DownloadId!;

        var snapshot = MediaCorrelator.Correlate(Input(
            radarr: records,
            history:
            [
                new ArrHistoryRecord
                {
                    DownloadId = downloadId, EventType = "downloadFailed",
                    Date = DateTimeOffset.UtcNow.AddMinutes(-30),
                },
                new ArrHistoryRecord
                {
                    DownloadId = downloadId, EventType = "downloadFolderImported",
                    Date = DateTimeOffset.UtcNow.AddMinutes(-5),
                },
            ]));

        var download = Assert.Single(snapshot.Journeys[0].Downloads);
        Assert.Equal(TerminalOutcome.Imported, download.Terminal);
    }

    // ── Comportements transverses ────────────────────────────────────────────────────

    [Fact]
    public void Les_deux_etats_dun_cycle_se_reflètent_dans_les_telechargements()
    {
        var snapshot = MediaCorrelator.Correlate(Input(
            sonarr: Fixture.Queue(SonarrMixed),
            torrents: Fixture.Torrents(Torrents)));

        var journey = Assert.Single(snapshot.Journeys);
        Assert.Equal(2, journey.Downloads.Count);
        Assert.Contains(journey.Downloads, d => d.State == DownloadState.Downloading);
        Assert.Contains(journey.Downloads, d => d.State == DownloadState.Importing);

        // Tant qu'un téléchargement est en cours, le parcours l'est aussi.
        Assert.Equal(JourneyState.Downloading, journey.State);
    }

    [Fact]
    public void Un_telechargement_qui_vient_de_demarrer_nest_pas_declare_en_progression()
    {
        // Le faux positif de la première seconde : sizeleft == size, donc rien n'a progressé.
        // C'est ce que les détecteurs devront regarder avant de crier au blocage.
        var snapshot = MediaCorrelator.Correlate(Input(
            radarr: Fixture.Queue("Lifecycle/01-radarr-queue-warning-au-demarrage.json")));

        var download = Assert.Single(snapshot.Journeys[0].Downloads);
        Assert.False(download.HasProgressed);
        Assert.Equal(DownloadHealth.Ok, download.Health);
    }

    [Fact]
    public void Un_torrent_absent_de_qBittorrent_ne_fait_pas_echouer_la_correlation()
    {
        // Le cas observé en capture : un downloadId sur cinq n'avait plus de torrent.
        var snapshot = MediaCorrelator.Correlate(Input(
            radarr: Fixture.Queue(RadarrDownloading),
            torrents: []));

        var download = Assert.Single(snapshot.Journeys[0].Downloads);
        Assert.Null(download.Torrent);
        Assert.Equal(DownloadState.Downloading, download.State);
    }

    [Fact]
    public void Une_file_vide_ne_produit_aucun_parcours()
    {
        var snapshot = MediaCorrelator.Correlate(Input(
            radarr: Fixture.Queue("Lifecycle/00-radarr-queue-vide.json"),
            sonarr: Fixture.Queue("Lifecycle/10-sonarr-queue-vide.json")));

        Assert.Empty(snapshot.Journeys);
        Assert.Equal(0, snapshot.BytesTotal);
    }
}
