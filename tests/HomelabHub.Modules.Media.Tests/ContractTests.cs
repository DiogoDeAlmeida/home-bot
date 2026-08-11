using HomelabHub.Modules.Media.Contracts;
using Xunit;

namespace HomelabHub.Modules.Media.Tests;

/// <summary>
/// Les modèles désérialisent-ils correctement les <b>vraies</b> réponses des services ?
/// </summary>
/// <remarks>
/// Radarr est en majeure 6 et Sonarr en 4.0.19 : ces tests sont ce qui préviendra qu'une montée
/// de version a changé la forme d'une réponse, au lieu de le découvrir par un tableau de bord
/// vide un dimanche soir.
/// </remarks>
public sealed class ContractTests
{
    private const string SonarrDownloading = "Lifecycle/12-sonarr-queue-deux-packs-downloading.json";
    private const string SonarrMixed = "Lifecycle/13-sonarr-queue-downloading-et-importpending.json";
    private const string RadarrDownloading = "Lifecycle/02-radarr-queue-downloading.json";
    private const string RadarrWarning = "Lifecycle/01-radarr-queue-warning-au-demarrage.json";
    private const string Torrents = "Lifecycle/21-qbittorrent-downloading.json";

    private static readonly string[] MediaTypes = ["movie", "tv"];

    [Fact]
    public void Une_entree_de_file_Radarr_se_deserialise_entierement()
    {
        var record = Assert.Single(Fixture.Queue(RadarrDownloading));

        Assert.NotNull(record.DownloadId);
        Assert.Equal(40, record.DownloadId!.Length);
        Assert.Equal("downloading", record.Status);
        Assert.Equal("downloading", record.TrackedDownloadState);
        Assert.Equal("ok", record.TrackedDownloadStatus);
        Assert.Equal("torrent", record.Protocol);
        Assert.True(record.Size > 0);
        Assert.True(record.SizeLeft > 0);
        Assert.NotNull(record.Added);

        // Objet joint par includeMovie=true : évite un appel par média pour obtenir le tmdbId.
        Assert.NotNull(record.Movie);
        Assert.Equal(record.MovieId, record.Movie!.Id);
        Assert.True(record.Movie.TmdbId > 0);
    }

    [Fact]
    public void Une_entree_de_file_Sonarr_porte_son_episode_et_sa_serie()
    {
        var record = Fixture.Queue(SonarrDownloading)[0];

        Assert.NotNull(record.SeriesId);
        Assert.NotNull(record.EpisodeId);
        Assert.NotNull(record.Episode);
        Assert.NotNull(record.Series);
        Assert.Equal(record.EpisodeId, record.Episode!.Id);
        Assert.Equal(record.SeriesId, record.Series!.Id);
        Assert.True(record.Series.TvdbId > 0);
    }

    [Fact]
    public void Un_torrent_qBittorrent_se_deserialise_malgre_le_snake_case()
    {
        // Seule API du lot à ne pas être en camelCase : sans attributs explicites, tous les
        // champs seraient silencieusement à zéro — panne invisible par excellence.
        var torrent = Fixture.Torrents(Torrents)[0];

        Assert.Equal(40, torrent.Hash.Length);
        Assert.NotNull(torrent.State);
        Assert.True(torrent.Size > 0);
        Assert.Contains(Fixture.Torrents(Torrents), t => t.DownloadSpeed > 0);
        Assert.Contains(Fixture.Torrents(Torrents), t => t.AddedOn > 0);
        Assert.Contains(Fixture.Torrents(Torrents), t => !string.IsNullOrEmpty(t.Category));
    }

    [Fact]
    public void Le_hash_v1_coincide_avec_le_hash_sur_les_torrents_v1()
    {
        // Vérifié sur l'installation : aucun torrent BitTorrent v2. Si ce test se met à échouer,
        // c'est qu'un torrent v2 est apparu et que la jointure doit basculer sur infohash_v1.
        foreach (var torrent in Fixture.Torrents(Torrents).Where(t => !string.IsNullOrEmpty(t.InfohashV1)))
        {
            Assert.Equal(torrent.Hash, torrent.InfohashV1);
        }
    }

    [Fact]
    public void Une_eta_inconnue_nest_pas_presentee_comme_une_duree()
    {
        // qBittorrent code « inconnu » par 8 640 000 secondes, soit cent jours. L'afficher tel
        // quel donnerait « fin dans 100 jours » sur un torrent en seed.
        var unknown = new QBittorrentTorrent { Eta = 8_640_000 };
        var known = new QBittorrentTorrent { Eta = 300 };

        Assert.Null(unknown.EstimatedTimeLeft);
        Assert.Equal(TimeSpan.FromMinutes(5), known.EstimatedTimeLeft);
    }

    [Fact]
    public void Le_statut_warning_du_demarrage_ne_signale_aucune_erreur_reelle()
    {
        // Piège constaté en capture : un torrent tout juste récupéré se déclare « stalled with
        // no connections » avant d'avoir trouvé un pair. C'est trackedDownloadStatus qui dit la
        // vérité, pas status ni errorMessage (ADR-0015).
        var record = Assert.Single(Fixture.Queue(RadarrWarning));

        Assert.Equal("warning", record.Status);
        Assert.Contains("stalled", record.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ok", record.TrackedDownloadStatus);
        Assert.Equal(record.Size, record.SizeLeft);
    }

    [Fact]
    public void Les_deux_etats_du_cycle_coexistent_dans_une_meme_reponse()
    {
        var records = Fixture.Queue(SonarrMixed);

        Assert.Equal(44, records.Count);
        Assert.Equal(22, records.Count(r => r.TrackedDownloadState == "downloading"));
        Assert.Equal(22, records.Count(r => r.TrackedDownloadState == "importPending"));

        var pending = records.First(r => r.TrackedDownloadState == "importPending");
        Assert.Equal("completed", pending.Status);
        Assert.Equal(0, pending.SizeLeft);
    }

    [Fact]
    public void Une_requete_Seerr_porte_ses_cles_de_jointure()
    {
        var requests = Fixture.Requests("Lifecycle/31-seerr-requetes-avec-downloadstatus.json");

        Assert.NotEmpty(requests);
        Assert.All(requests, request =>
        {
            Assert.NotNull(request.Media);
            Assert.Contains(request.Media!.MediaType, MediaTypes);
        });

        // Une requête peut porter plusieurs saisons : trois dans les données observées. C'est le
        // cas limite du pack de saison, présent sans avoir eu besoin de le fabriquer.
        Assert.Contains(requests, r => r.Seasons.Count > 1);
    }
}
