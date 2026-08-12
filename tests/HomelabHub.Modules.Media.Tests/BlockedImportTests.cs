using HomelabHub.Modules.Media.Contracts;
using HomelabHub.Modules.Media.Correlation;
using HomelabHub.Modules.Media.Detection;
using Xunit;

namespace HomelabHub.Modules.Media.Tests;

/// <summary>
/// L'import bloqué, capturé sur un cas réel le 12 août 2026.
/// </summary>
/// <remarks>
/// <para>
/// Ce cas ne se provoque pas sur commande : il est né d'un <c>stop</c> puis <c>start</c>
/// manuels dans qBittorrent, contournant le pilotage de Radarr. Il apporte la seule chose qui
/// manquait encore — la sémantique de <c>statusMessages</c>, jusque-là documentée comme
/// inconnue.
/// </para>
/// <para>
/// Ce que la capture a établi, et que ces tests figent :
/// </para>
/// <list type="bullet">
///   <item><c>trackedDownloadState</c> vaut <c>importBlocked</c>, une valeur jamais observée
///         auparavant ;</item>
///   <item><c>errorMessage</c> est <b>vide</b> — l'explication n'est pas là ;</item>
///   <item>l'historique ne contient que <c>grabbed</c>, aucun événement terminal ;</item>
///   <item>les journaux de Radarr n'en gardent aucune trace, à aucun niveau ;</item>
///   <item><c>statusMessages</c> porte donc <b>la seule explication existante</b>.</item>
/// </list>
/// </remarks>
public sealed class BlockedImportTests
{
    private const string Queue = "Lifecycle/40-radarr-queue-import-bloque.json";
    private const string History = "Lifecycle/41-radarr-history-import-bloque.json";
    private const string Torrents = "Lifecycle/43-qbittorrent-import-bloque.json";

    private static MediaSnapshot Snapshot() =>
        MediaCorrelator.Correlate(new CorrelationInput(
            RadarrQueue: Fixture.Queue(Queue),
            SonarrQueue: [],
            RadarrHistory: Fixture.Load<ArrPage<ArrHistoryRecord>>(History).Records,
            SonarrHistory: [],
            Torrents: Fixture.Torrents(Torrents),
            Requests: [],
            ObservedAt: DateTimeOffset.UtcNow,
            UnavailableSources: []));

    [Fact]
    public void Un_import_bloque_se_lit_dans_letat_pas_dans_errorMessage()
    {
        var record = Assert.Single(Fixture.Queue(Queue));

        Assert.Equal("completed", record.Status);
        Assert.Equal("importBlocked", record.TrackedDownloadState);
        Assert.Equal("warning", record.TrackedDownloadStatus);
        Assert.Equal(0, record.SizeLeft);

        // Le champ qui porte « erreur » dans son nom est vide. Toute la valeur est ailleurs.
        Assert.True(string.IsNullOrEmpty(record.ErrorMessage));
        Assert.Single(record.StatusMessages);
    }

    [Fact]
    public void Lhistorique_reste_muet_sur_un_import_bloque()
    {
        // Aucun événement terminal : le blocage n'est pas une fin de cycle pour Radarr.
        // C'est pourquoi l'état terminal ne peut pas servir à détecter ce cas.
        var history = Fixture.Load<ArrPage<ArrHistoryRecord>>(History).Records;

        Assert.All(history, record => Assert.Equal("grabbed", record.EventType));
        Assert.DoesNotContain(history, r => r.EventType == "downloadFailed");
    }

    [Fact]
    public void ImportBlocked_est_traite_comme_un_import_en_cours()
    {
        var download = Assert.Single(Snapshot().Journeys[0].Downloads);

        Assert.Equal(DownloadState.Importing, download.State);
        Assert.Equal(DownloadHealth.Warning, download.Health);
        Assert.Equal(1d, download.Progress);
    }

    [Fact]
    public void Le_parcours_demande_une_intervention()
    {
        var journey = Assert.Single(Snapshot().Journeys);

        Assert.True(journey.NeedsAttention);
        Assert.Equal(JourneyState.Importing, journey.State);
    }

    [Fact]
    public void Lexplication_du_service_est_restituee_mot_pour_mot()
    {
        // Restituée, jamais analysée : c'est une phrase produite par Radarr, pas un code
        // d'erreur. Sa formulation peut changer d'une version à l'autre.
        var download = Assert.Single(Snapshot().Journeys[0].Downloads);

        var message = Assert.Single(download.StatusMessages);
        Assert.Contains("Manual Import required", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Lanomalie_publie_lexplication_du_service_plutot_quun_texte_generique()
    {
        var events = MediaDetectors.Detect(
            Snapshot(),
            new DetectionThresholds(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10)),
            DateTimeOffset.UtcNow);

        var anomaly = Assert.Single(events, e => e.Type == "media.import.pending");
        Assert.Contains("Manual Import required", anomaly.Body!, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_torrent_termine_et_en_seed_ne_declenche_pas_le_detecteur_de_blocage()
    {
        // Le torrent est à 100 % en stalledUP depuis des heures : « inactif » au sens de
        // qBittorrent. Mais ce n'est pas un téléchargement bloqué — c'est un import bloqué,
        // et confondre les deux produirait deux anomalies pour un seul problème.
        var events = MediaDetectors.Detect(
            Snapshot(),
            new DetectionThresholds(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10)),
            DateTimeOffset.UtcNow);

        Assert.DoesNotContain(events, e => e.Type == "media.download.stalled");
    }
}
