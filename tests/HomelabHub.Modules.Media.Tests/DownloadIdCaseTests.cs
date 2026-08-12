using HomelabHub.Modules.Media.Contracts;
using HomelabHub.Modules.Media.Correlation;
using HomelabHub.Modules.Media.Detection;
using Xunit;

namespace HomelabHub.Modules.Media.Tests;

/// <summary>
/// La casse du <c>downloadId</c> : normaliser pour joindre, conserver pour interroger.
/// </summary>
/// <remarks>
/// <para>
/// Régression vécue. Une version antérieure ne gardait que la forme normalisée en minuscules et
/// la réutilisait pour interroger Radarr. Or ses routes filtrées par <c>downloadId</c> sont
/// <b>sensibles à la casse</b> : vérifié sur l'instance réelle, la même requête renvoie
/// 1 candidat en majuscules et 0 en minuscules.
/// </para>
/// <para>
/// Le symptôme était le pire possible : aucune erreur, aucun journal, une réponse vide
/// indiscernable d'un « rien à signaler ». Le repli d'historique a fonctionné à vide pendant
/// plusieurs tranches sans que rien ne le signale, et le bouton d'import répondait « aucun
/// fichier à importer » sur un fichier parfaitement importable.
/// </para>
/// </remarks>
public sealed class DownloadIdCaseTests
{
    private const string Upper = "A87081E769ACBD5EE514CA6AF03A478AF27FC8C5";

    private static MediaSnapshot Correlate(string downloadId) =>
        MediaCorrelator.Correlate(new CorrelationInput(
            [new ArrQueueRecord
            {
                DownloadId = downloadId,
                MovieId = 50,
                TrackedDownloadState = "importBlocked",
                TrackedDownloadStatus = "warning",
            }],
            [], [], [],
            [new QBittorrentTorrent { Hash = downloadId.ToLowerInvariant(), State = "stalledUP" }],
            [], DateTimeOffset.UtcNow, []));

    [Fact]
    public void Le_downloadId_conserve_la_casse_donnee_par_le_service()
    {
        var download = Assert.Single(Correlate(Upper).Journeys[0].Downloads);

        Assert.Equal(Upper, download.DownloadId);
        Assert.Equal(Upper.ToLowerInvariant(), download.JoinKey);
    }

    [Fact]
    public void La_jointure_avec_le_torrent_passe_malgre_la_difference_de_casse()
    {
        // Le service donne des majuscules, qBittorrent des minuscules : c'est précisément
        // pourquoi la clé de jointure existe séparément.
        var download = Assert.Single(Correlate(Upper).Journeys[0].Downloads);

        Assert.NotNull(download.Torrent);
        Assert.Equal(download.JoinKey, download.Torrent!.JoinKey);
    }

    [Fact]
    public void Les_cles_de_deduplication_sont_normalisees()
    {
        // Les clés internes se comparent entre elles : elles doivent être stables, donc
        // normalisées. Ce sont les requêtes sortantes qui exigent la forme d'origine.
        var events = MediaDetectors.Detect(
            Correlate(Upper),
            new DetectionThresholds(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10)),
            DateTimeOffset.UtcNow);

        var anomaly = Assert.Single(events);
        Assert.EndsWith(Upper.ToLowerInvariant(), anomaly.DedupeKey!, StringComparison.Ordinal);
        Assert.Equal(Upper.ToLowerInvariant(), anomaly.Data!["downloadId"]);
    }

    [Fact]
    public void Un_service_qui_renverrait_des_minuscules_joindrait_aussi()
    {
        // Rien ne garantit que tous les services majusculent. La normalisation doit fonctionner
        // dans les deux sens, sans quoi on remplacerait une hypothèse par une autre.
        var download = Assert.Single(Correlate(Upper.ToLowerInvariant()).Journeys[0].Downloads);

        Assert.NotNull(download.Torrent);
        Assert.Equal(Upper.ToLowerInvariant(), download.JoinKey);
    }
}
