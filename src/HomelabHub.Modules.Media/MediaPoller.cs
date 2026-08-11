using HomelabHub.Abstractions.Configuration;
using HomelabHub.Abstractions.Ingest;
using HomelabHub.Abstractions.Modules;
using HomelabHub.Modules.Media.Clients;
using HomelabHub.Modules.Media.Contracts;
using HomelabHub.Modules.Media.Correlation;

namespace HomelabHub.Modules.Media;

/// <summary>
/// Interroge les quatre services et reconstruit le snapshot.
/// </summary>
/// <remarks>
/// <para>
/// Le cycle lit tout, puis corrèle. Il ne consulte jamais le snapshot précédent : l'état est
/// dérivé intégralement à chaque passage (ADR-0015).
/// </para>
/// <para>
/// Les quatre lectures partent <b>en parallèle</b> : elles sont indépendantes, et les
/// enchaîner ferait durer le cycle de la somme de leurs latences. Un service muet n'interrompt
/// pas les autres — il est signalé dans <c>UnavailableSources</c>, pour que l'affichage puisse
/// dire « Radarr injoignable » au lieu de « aucun téléchargement ».
/// </para>
/// </remarks>
internal sealed class MediaPoller(
    IRadarrClient radarr,
    ISonarrClient sonarr,
    ISeerrClient seerr,
    IQBittorrentClient qbittorrent,
    IModuleState<MediaSnapshot> state,
    IModuleConfiguration<MediaModule> config) : IModulePoller
{
    public async Task PollAsync(CancellationToken cancellationToken)
    {
        var historyPageSize = config.GetInt32(MediaModule.HistoryPageSizeKey, 100);

        var radarrQueue = radarr.GetQueueAsync(cancellationToken);
        var sonarrQueue = sonarr.GetQueueAsync(cancellationToken);
        var radarrHistory = radarr.GetRecentHistoryAsync(historyPageSize, cancellationToken);
        var sonarrHistory = sonarr.GetRecentHistoryAsync(historyPageSize, cancellationToken);
        var torrents = qbittorrent.GetTorrentsAsync(cancellationToken);
        var requests = seerr.GetRecentRequestsAsync(50, cancellationToken);

        await Task.WhenAll(radarrQueue, sonarrQueue, radarrHistory, sonarrHistory, torrents, requests)
                  .ConfigureAwait(false);

        var unavailable = new List<string>();
        Collect(radarrQueue.Result, "Radarr", unavailable);
        Collect(sonarrQueue.Result, "Sonarr", unavailable);
        Collect(torrents.Result, "qBittorrent", unavailable);
        Collect(requests.Result, "Seerr", unavailable);

        var input = new CorrelationInput(
            RadarrQueue: radarrQueue.Result.OrEmpty(),
            SonarrQueue: sonarrQueue.Result.OrEmpty(),
            RadarrHistory: radarrHistory.Result.OrEmpty(),
            SonarrHistory: sonarrHistory.Result.OrEmpty(),
            Torrents: torrents.Result.OrEmpty(),
            Requests: requests.Result.OrEmpty(),
            ObservedAt: DateTimeOffset.UtcNow,
            UnavailableSources: unavailable);

        var snapshot = MediaCorrelator.Correlate(input);

        // Un second passage, uniquement si des issues manquent : on complète l'historique par
        // des requêtes ciblées, puis on recorrèle. Recorréler plutôt que rapiécer le snapshot
        // garde le corrélateur comme unique endroit où l'état se déduit.
        var extra = await FetchMissingHistoryAsync(snapshot, cancellationToken).ConfigureAwait(false);
        if (extra.Count > 0)
        {
            snapshot = MediaCorrelator.Correlate(input with
            {
                RadarrHistory = [.. input.RadarrHistory, .. extra],
                SonarrHistory = input.SonarrHistory,
            });
        }

        state.Mutate(_ => snapshot);
    }

    /// <summary>
    /// Repli ciblé pour les parcours dont l'issue n'est pas lisible dans la page d'historique.
    /// </summary>
    /// <remarks>
    /// Une page a une taille finie : si plusieurs imports surviennent entre deux cycles, ou si un
    /// cycle échoue et que le suivant arrive tard, l'événement terminal peut en être sorti. Sans
    /// ce repli, le parcours resterait <c>Unresolved</c> indéfiniment.
    /// <para>
    /// Il ne se déclenche que sur ce cas précis, donc rarement. S'il se déclenche à chaque
    /// cycle, c'est le signe que la page d'historique est sous-dimensionnée.
    /// </para>
    /// </remarks>
    private async Task<List<ArrHistoryRecord>> FetchMissingHistoryAsync(
        MediaSnapshot snapshot, CancellationToken cancellationToken)
    {
        var unresolved = snapshot.Journeys
            .Where(j => j.State == JourneyState.Unresolved)
            .SelectMany(j => j.Downloads.Where(d => d.Terminal is null)
                                        .Select(d => (Journey: j, d.DownloadId)))
            // Borne dure : si des dizaines de parcours sont indéterminés, le problème est la
            // taille de la page d'historique, pas le nombre de requêtes à lancer.
            .Take(10)
            .ToList();

        var extra = new List<ArrHistoryRecord>();

        foreach (var (journey, downloadId) in unresolved)
        {
            // Interroger la seule instance concernée : un film n'a rien à faire chez Sonarr.
            IArrClient client = journey.MediaType == MediaKind.Movie ? radarr : sonarr;
            var result = await client.GetHistoryForDownloadAsync(downloadId, cancellationToken)
                                     .ConfigureAwait(false);

            if (result.Success)
            {
                extra.AddRange(result.OrEmpty());
            }
        }

        return extra;
    }

    private static void Collect<T>(ServiceResult<T> result, string service, List<string> unavailable)
    {
        if (!result.Success)
        {
            unavailable.Add($"{service} : {result.Error}");
        }
    }
}
