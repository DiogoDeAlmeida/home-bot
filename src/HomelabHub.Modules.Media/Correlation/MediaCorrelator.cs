using System.Globalization;
using HomelabHub.Modules.Media.Contracts;

namespace HomelabHub.Modules.Media.Correlation;

/// <summary>Ce que le corrélateur a besoin de lire, pour un cycle.</summary>
/// <param name="RadarrQueue">File Radarr.</param>
/// <param name="SonarrQueue">File Sonarr.</param>
/// <param name="RadarrHistory">Page d'historique Radarr, lue une fois par cycle.</param>
/// <param name="SonarrHistory">Page d'historique Sonarr, lue une fois par cycle.</param>
/// <param name="Torrents">Torrents connus de qBittorrent.</param>
/// <param name="Requests">Requêtes Seerr récentes.</param>
/// <param name="ObservedAt">Instant du cycle.</param>
/// <param name="UnavailableSources">Services n'ayant pas répondu, pour que l'affichage le dise.</param>
public sealed record CorrelationInput(
    IReadOnlyList<ArrQueueRecord> RadarrQueue,
    IReadOnlyList<ArrQueueRecord> SonarrQueue,
    IReadOnlyList<ArrHistoryRecord> RadarrHistory,
    IReadOnlyList<ArrHistoryRecord> SonarrHistory,
    IReadOnlyList<QBittorrentTorrent> Torrents,
    IReadOnlyList<SeerrRequest> Requests,
    DateTimeOffset ObservedAt,
    IReadOnlyList<string> UnavailableSources);

/// <summary>
/// Rapproche requêtes, files et torrents en une vue unique par média.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fonction pure.</b> Aucun appel réseau, aucun état conservé entre deux cycles : elle prend
/// ce que les services disent et en déduit tout (ADR-0015). C'est ce qui la rend testable
/// directement contre les réponses capturées, sans HTTP ni doublure.
/// </para>
/// <para>
/// L'ordre des opérations n'est pas indifférent. <b>Le regroupement par <c>downloadId</c> vient
/// en premier</b>, avant toute agrégation : un pack de saison produit une entrée de file par
/// épisode, avec la même taille répétée, et sommer avant de regrouper donne 451 Go là où il y
/// en a 20.
/// </para>
/// </remarks>
public static class MediaCorrelator
{
    public static MediaSnapshot Correlate(CorrelationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var torrents = BuildTorrentIndex(input.Torrents);
        var history = BuildHistoryIndex(input.RadarrHistory.Concat(input.SonarrHistory));
        var requests = BuildRequestIndex(input.Requests);

        var journeys = new List<MediaJourney>();
        journeys.AddRange(BuildJourneys(input.RadarrQueue, MediaKind.Movie, torrents, history, requests));
        journeys.AddRange(BuildJourneys(input.SonarrQueue, MediaKind.Series, torrents, history, requests));

        // Un média demandé dont rien n'est encore dans la file — ou dont tout est déjà importé —
        // n'apparaît dans aucune file. Il a pourtant un parcours.
        journeys.AddRange(BuildJourneysWithoutQueue(input.Requests, journeys));

        return new MediaSnapshot(
            [.. journeys.OrderByDescending(j => j.State == JourneyState.Downloading)
                        .ThenBy(j => j.Title, StringComparer.OrdinalIgnoreCase)],
            input.UnavailableSources,
            input.ObservedAt);
    }

    /// <summary>
    /// Indexe les torrents par leur clé de jointure normalisée.
    /// </summary>
    /// <remarks>
    /// Les <c>*arr</c> renvoient le hash en majuscules, qBittorrent en minuscules : la
    /// normalisation est la seule chose qui sépare une corrélation qui marche d'une corrélation
    /// qui ne trouve jamais rien.
    /// </remarks>
    private static Dictionary<string, QBittorrentTorrent> BuildTorrentIndex(
        IReadOnlyList<QBittorrentTorrent> torrents)
    {
        var index = new Dictionary<string, QBittorrentTorrent>(StringComparer.Ordinal);

        foreach (var torrent in torrents)
        {
            if (!string.IsNullOrEmpty(torrent.JoinKey))
            {
                index[torrent.JoinKey] = torrent;
            }

            // Repli pour un éventuel torrent BitTorrent v2, dont le hash affiché pourrait ne
            // plus correspondre au downloadId des *arr.
            if (!string.IsNullOrEmpty(torrent.InfohashV1))
            {
                index.TryAdd(torrent.InfohashV1.ToLowerInvariant(), torrent);
            }
        }

        return index;
    }

    /// <summary>
    /// Issue terminale par <c>downloadId</c>, telle que l'historique la rapporte.
    /// </summary>
    /// <remarks>
    /// L'historique porte la même duplication par épisode que la file — 44 événements pour un
    /// pack. On ne garde donc qu'une issue par téléchargement, la plus significative.
    /// </remarks>
    private static Dictionary<string, TerminalOutcome> BuildHistoryIndex(
        IEnumerable<ArrHistoryRecord> history)
    {
        var index = new Dictionary<string, TerminalOutcome>(StringComparer.Ordinal);

        foreach (var record in history.OrderBy(r => r.Date))
        {
            if (string.IsNullOrEmpty(record.DownloadId))
            {
                continue;
            }

            var outcome = record.EventType switch
            {
                "downloadFolderImported" => TerminalOutcome.Imported,
                "downloadFailed" => TerminalOutcome.Failed,
                "downloadIgnored" => TerminalOutcome.Ignored,
                _ => (TerminalOutcome?)null,
            };

            // Le tri chronologique fait que le dernier événement terminal l'emporte : une
            // release réessayée puis importée n'est pas comptée comme échouée.
            if (outcome is not null)
            {
                index[record.DownloadId.ToLowerInvariant()] = outcome.Value;
            }
        }

        return index;
    }

    private static Dictionary<string, SeerrRequest> BuildRequestIndex(IReadOnlyList<SeerrRequest> requests)
    {
        var index = new Dictionary<string, SeerrRequest>(StringComparer.Ordinal);

        foreach (var request in requests.Where(r => r.Media?.ExternalServiceId is not null))
        {
            var kind = string.Equals(request.Media!.MediaType, "movie", StringComparison.OrdinalIgnoreCase)
                ? MediaKind.Movie
                : MediaKind.Series;

            index.TryAdd(JourneyKey(kind, request.Media.ExternalServiceId!.Value), request);
        }

        return index;
    }

    private static IEnumerable<MediaJourney> BuildJourneys(
        IReadOnlyList<ArrQueueRecord> queue,
        MediaKind kind,
        Dictionary<string, QBittorrentTorrent> torrents,
        Dictionary<string, TerminalOutcome> history,
        Dictionary<string, SeerrRequest> requests)
    {
        // Étape 1 — regrouper par média. Un pack de saison couvre plusieurs épisodes d'une même
        // série : ils appartiennent tous au même parcours.
        var byMedia = queue
            .Where(r => MediaIdOf(r, kind) is not null)
            .GroupBy(r => MediaIdOf(r, kind)!.Value);

        foreach (var media in byMedia)
        {
            // Étape 2 — regrouper par téléchargement, AVANT toute agrégation de taille.
            var downloads = media
                .Where(r => !string.IsNullOrEmpty(r.DownloadId))
                .GroupBy(r => r.DownloadId!.ToLowerInvariant(), StringComparer.Ordinal)
                .Select(group => BuildDownload(group, torrents, history))
                .ToList();

            var key = JourneyKey(kind, media.Key);
            requests.TryGetValue(key, out var request);

            yield return new MediaJourney(
                Key: key,
                MediaType: kind,
                Title: TitleOf(media.First()),
                TmdbId: media.First().Movie?.TmdbId ?? media.First().Series?.TmdbId,
                TvdbId: media.First().Series?.TvdbId,
                Request: ToRequest(request),
                Downloads: downloads,
                State: DeriveJourneyState(downloads, request));
        }
    }

    /// <summary>
    /// Parcours d'un média demandé mais absent des files : pas encore récupéré, ou déjà importé.
    /// </summary>
    /// <remarks>
    /// C'est le cas « média déjà présent » : Seerr le marque disponible sans qu'aucun
    /// téléchargement n'ait jamais eu lieu. Il ne doit produire ni anomalie, ni téléchargement
    /// fantôme — seulement un parcours sans téléchargement.
    /// </remarks>
    private static IEnumerable<MediaJourney> BuildJourneysWithoutQueue(
        IReadOnlyList<SeerrRequest> requests,
        IReadOnlyList<MediaJourney> existing)
    {
        var known = existing.Select(j => j.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var request in requests)
        {
            if (request.Media?.ExternalServiceId is not { } externalId)
            {
                continue;
            }

            var kind = string.Equals(request.Media.MediaType, "movie", StringComparison.OrdinalIgnoreCase)
                ? MediaKind.Movie
                : MediaKind.Series;

            var key = JourneyKey(kind, externalId);
            if (!known.Add(key))
            {
                continue;
            }

            // Statut Seerr : 5 = disponible, 4 = partiellement disponible.
            var state = request.Media.Status switch
            {
                5 => JourneyState.Available,
                4 => JourneyState.Available,
                _ => JourneyState.Requested,
            };

            yield return new MediaJourney(
                Key: key,
                MediaType: kind,
                Title: null,
                TmdbId: request.Media.TmdbId,
                TvdbId: request.Media.TvdbId,
                Request: ToRequest(request),
                Downloads: [],
                State: state);
        }
    }

    private static DownloadItem BuildDownload(
        IGrouping<string, ArrQueueRecord> group,
        Dictionary<string, QBittorrentTorrent> torrents,
        Dictionary<string, TerminalOutcome> history)
    {
        // La taille est prise SUR UNE SEULE entrée : les 22 entrées d'un pack la répètent à
        // l'identique, les sommer multiplierait le total par 22 (ADR-0015).
        var first = group.First();

        var episodes = group
            .Where(r => r.EpisodeId is not null)
            .Select(r => new EpisodeReference(
                r.EpisodeId!.Value,
                r.Episode?.SeasonNumber ?? r.SeasonNumber ?? 0,
                r.Episode?.EpisodeNumber ?? 0))
            .DistinctBy(e => e.EpisodeId)
            .OrderBy(e => e.SeasonNumber)
            .ThenBy(e => e.EpisodeNumber)
            .ToList();

        torrents.TryGetValue(group.Key, out var torrent);
        history.TryGetValue(group.Key, out var terminal);

        return new DownloadItem(
            DownloadId: group.Key,
            Title: first.Title,
            Size: first.Size,
            SizeLeft: first.SizeLeft,
            State: DeriveDownloadState(group),
            Health: DeriveHealth(group),
            Torrent: torrent,
            Episodes: episodes,
            AddedAt: first.Added,
            Terminal: history.ContainsKey(group.Key) ? terminal : null,
            // Restitués tels quels, dédupliqués : les 22 entrées d'un pack répètent le même
            // message. On ne les interprète pas — la gravité vient de trackedDownloadStatus.
            StatusMessages: [.. group.SelectMany(r => r.StatusMessages)
                                     .SelectMany(m => m.Messages)
                                     .Where(m => !string.IsNullOrWhiteSpace(m))
                                     .Distinct(StringComparer.Ordinal)]);
    }

    /// <summary>
    /// Étape du cycle. Les entrées d'un même téléchargement peuvent diverger transitoirement :
    /// l'état le plus avancé l'emporte.
    /// </summary>
    private static DownloadState DeriveDownloadState(IEnumerable<ArrQueueRecord> group) =>
        group.Select(r => r.TrackedDownloadState switch
        {
            "downloading" => DownloadState.Downloading,
            "importPending" or "importing" or "importBlocked" => DownloadState.Importing,
            "imported" => DownloadState.Completed,
            _ => DownloadState.Unknown,
        }).Min();

    /// <summary>
    /// Santé, dérivée du seul champ fiable.
    /// </summary>
    /// <remarks>
    /// Ni <c>status</c> ni <c>errorMessage</c> ne sont consultés : le premier vaut
    /// <c>warning</c> dès la première seconde, le second annonce « stalled with no
    /// connections » sur un téléchargement sain. <c>statusMessages</c> reste inexploité tant
    /// que sa sémantique n'est pas établie (ADR-0015).
    /// </remarks>
    private static DownloadHealth DeriveHealth(IEnumerable<ArrQueueRecord> group) =>
        group.Select(r => r.TrackedDownloadStatus switch
        {
            "ok" => DownloadHealth.Ok,
            "warning" => DownloadHealth.Warning,
            "error" => DownloadHealth.Error,
            _ => DownloadHealth.Unknown,
        }).Max();

    private static JourneyState DeriveJourneyState(List<DownloadItem> downloads, SeerrRequest? request)
    {
        if (downloads.Count == 0)
        {
            return request?.Media?.Status is 4 or 5 ? JourneyState.Available : JourneyState.Requested;
        }

        if (downloads.Any(d => d.State == DownloadState.Downloading))
        {
            return JourneyState.Downloading;
        }

        if (downloads.Any(d => d.State == DownloadState.Importing))
        {
            return JourneyState.Importing;
        }

        if (downloads.All(d => d.Terminal == TerminalOutcome.Imported))
        {
            return JourneyState.Available;
        }

        if (downloads.Any(d => d.Terminal is TerminalOutcome.Failed or TerminalOutcome.Ignored))
        {
            return JourneyState.Failed;
        }

        return JourneyState.Unresolved;
    }

    private static MediaRequest? ToRequest(SeerrRequest? request) =>
        request is null
            ? null
            : new MediaRequest(
                request.Id,
                request.CreatedAt,
                [.. request.Seasons.Select(s => s.SeasonNumber).OrderBy(n => n)]);

    private static int? MediaIdOf(ArrQueueRecord record, MediaKind kind) =>
        kind == MediaKind.Movie ? record.MovieId : record.SeriesId;

    private static string? TitleOf(ArrQueueRecord record) => record.Title;

    private static string JourneyKey(MediaKind kind, int mediaId) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{(kind == MediaKind.Movie ? "movie" : "series")}:{mediaId}");
}
