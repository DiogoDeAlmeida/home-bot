using HomelabHub.Abstractions.Configuration;
using HomelabHub.Abstractions.Events;
using HomelabHub.Abstractions.Ingest;
using HomelabHub.Abstractions.Modules;
using HomelabHub.Modules.Media.Clients;
using HomelabHub.Modules.Media.Contracts;
using HomelabHub.Modules.Media.Correlation;
using HomelabHub.Modules.Media.Detection;

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
    IModuleConfiguration<MediaModule> config,
    IEventPublisher events) : IModulePoller
{
    /// <summary>
    /// Cycles consécutifs pendant lesquels chaque service est resté muet.
    /// </summary>
    /// <remarks>
    /// <b>Seul état conservé entre deux cycles, et il est assumé.</b> Il ne décrit pas un média
    /// mais la liaison réseau, il vit en mémoire, il n'est jamais persisté, et le perdre au
    /// redémarrage ne coûte que deux cycles d'attente supplémentaires. ADR-0015 porte sur l'état
    /// dérivé des médias, pas sur un compteur de tentatives : rien ici ne peut diverger de ce
    /// que les services savent redire.
    /// </remarks>
    private readonly Dictionary<string, int> _consecutiveFailures = new(StringComparer.Ordinal);

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

        await PublishAnomaliesAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Republie l'ensemble de ce qui va mal.
    /// </summary>
    /// <remarks>
    /// Aucune anomalie n'est jamais fermée explicitement : ce qui cesse d'être republié est
    /// résolu par le noyau (ADR-0005). Les détecteurs sont une projection du snapshot, pas des
    /// émetteurs d'événements ponctuels.
    /// </remarks>
    private async Task PublishAnomaliesAsync(MediaSnapshot snapshot, CancellationToken cancellationToken)
    {
        var thresholds = new DetectionThresholds(
            StalledAfter: config.GetDuration(MediaModule.StalledAfterKey, TimeSpan.FromMinutes(30)),
            GraceAfterAdded: config.GetDuration(MediaModule.GraceAfterAddedKey, TimeSpan.FromMinutes(10)));

        var now = DateTimeOffset.UtcNow;

        foreach (var anomaly in MediaDetectors.Detect(snapshot, thresholds, now))
        {
            await events.PublishAsync(anomaly, cancellationToken).ConfigureAwait(false);
        }

        foreach (var anomaly in DetectUnreachableServices(snapshot, now))
        {
            await events.PublishAsync(anomaly, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Service muet depuis assez de cycles pour que ce ne soit plus un simple redémarrage.
    /// </summary>
    /// <remarks>
    /// Le seuil en cycles plutôt qu'en durée est délibéré : il suit automatiquement l'intervalle
    /// de polling. Passer le cycle de 60 à 10 secondes ne doit pas rendre l'alerte huit fois
    /// plus lente à apparaître.
    /// </remarks>
    private IEnumerable<HubEvent> DetectUnreachableServices(MediaSnapshot snapshot, DateTimeOffset now)
    {
        var required = Math.Max(1, config.GetInt32(MediaModule.UnreachableCyclesKey, 2));
        var failing = snapshot.UnavailableSources
            .Select(entry => entry.Split(' ', 2)[0])
            .ToHashSet(StringComparer.Ordinal);

        foreach (var service in new[] { "Radarr", "Sonarr", "Seerr", "qBittorrent" })
        {
            if (!failing.Contains(service))
            {
                _consecutiveFailures.Remove(service);
                continue;
            }

            var count = _consecutiveFailures.GetValueOrDefault(service) + 1;
            _consecutiveFailures[service] = count;

            if (count < required)
            {
                continue;
            }

            yield return new HubEvent(
                ModuleKey: "media",
                Type: "media.service.unreachable",
                Severity: HubEventSeverity.Critical,
                Title: $"{service} injoignable",
                Body: snapshot.UnavailableSources.FirstOrDefault(s => s.StartsWith(service, StringComparison.Ordinal))
                      ?? $"{service} n'a pas répondu sur {count} cycles consécutifs.",
                DedupeKey: $"media.service.unreachable:{service}",
                Data: new Dictionary<string, string>
                {
                    ["service"] = service,
                    ["cycles"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
                OccurredAt: now);
        }
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
            // DownloadId et non JoinKey : la route ?downloadId= de Radarr et Sonarr est
            // sensible à la casse, et une requête en minuscules revient vide sans erreur.
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
