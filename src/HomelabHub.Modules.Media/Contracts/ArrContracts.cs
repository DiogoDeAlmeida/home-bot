using System.Text.Json.Serialization;

namespace HomelabHub.Modules.Media.Contracts;

/// <summary>
/// Modèles de l'API v3, commune à Radarr 6.3 et Sonarr 4.0.
/// </summary>
/// <remarks>
/// <para>
/// Écrits contre des réponses <b>capturées sur les instances réelles</b>, jamais contre la
/// documentation : Radarr est en majeure 6, et les fixtures de
/// <c>tests/HomelabHub.Modules.Media.Tests/Fixtures</c> font foi.
/// </para>
/// <para>
/// Seuls les champs utiles à la corrélation sont modélisés. <c>System.Text.Json</c> ignore les
/// propriétés inconnues : la réponse réelle en porte des dizaines d'autres, inutiles ici.
/// </para>
/// </remarks>
public sealed record ArrPage<T>(
    int Page,
    int PageSize,
    int TotalRecords,
    IReadOnlyList<T> Records);

/// <summary>
/// Une entrée de file.
/// </summary>
/// <remarks>
/// <b>Attention à la multiplicité (ADR-0015).</b> Un pack de saison produit <i>un enregistrement
/// par épisode</i>, tous porteurs du même <see cref="DownloadId"/> et de la <b>même
/// <see cref="Size"/> répétée</b>. Observé : 22 enregistrements pour un torrent. Agréger sans
/// regrouper par <see cref="DownloadId"/> donne 451 Go là où il y en a 20.
/// </remarks>
public sealed record ArrQueueRecord
{
    public long Id { get; init; }

    /// <summary>
    /// Hash du torrent, <b>en majuscules</b>. qBittorrent le renvoie en minuscules : normaliser
    /// avant toute jointure. Nul pour un téléchargement dont le client ne rapporte pas d'identifiant.
    /// </summary>
    public string? DownloadId { get; init; }

    public string? Title { get; init; }

    /// <summary>
    /// Résumé de l'état — <c>warning</c>, <c>downloading</c>, <c>completed</c>.
    /// <b>Ce n'est pas un axe de santé</b> : il vaut <c>warning</c> dès la première seconde d'un
    /// téléchargement qui n'a pas encore trouvé de pair. Utiliser <see cref="TrackedDownloadStatus"/>.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>Étape du cycle : <c>downloading</c>, <c>importPending</c>, <c>importing</c>, <c>failedPending</c>.</summary>
    public string? TrackedDownloadState { get; init; }

    /// <summary>Santé réelle : <c>ok</c>, <c>warning</c>, <c>error</c>. <b>Le seul axe fiable.</b></summary>
    public string? TrackedDownloadStatus { get; init; }

    public string? Protocol { get; init; }

    public string? DownloadClient { get; init; }

    public string? Indexer { get; init; }

    public long Size { get; init; }

    public long SizeLeft { get; init; }

    /// <summary>Format <c>hh:mm:ss</c>. Absent tant que le débit est inconnu.</summary>
    public string? TimeLeft { get; init; }

    public DateTimeOffset? EstimatedCompletionTime { get; init; }

    /// <summary>Ajout à la file. Sert de base au délai de grâce des détecteurs (ADR-0015).</summary>
    public DateTimeOffset? Added { get; init; }

    /// <summary>
    /// <b>Ne pas interpréter comme une erreur.</b> Vaut « The download is stalled with no
    /// connections » dès la première seconde d'un téléchargement parfaitement sain.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Même méfiance que <see cref="ErrorMessage"/> : rien ne prouve encore qu'un contenu non
    /// vide signale un problème. À qualifier quand un import réellement bloqué aura été observé.
    /// </summary>
    public IReadOnlyList<ArrStatusMessage> StatusMessages { get; init; } = [];

    // ── Spécifique Radarr ────────────────────────────────────────────────────────────
    public int? MovieId { get; init; }

    public ArrMovie? Movie { get; init; }

    // ── Spécifique Sonarr ────────────────────────────────────────────────────────────
    public int? SeriesId { get; init; }

    public int? EpisodeId { get; init; }

    public int? SeasonNumber { get; init; }

    public ArrSeries? Series { get; init; }

    public ArrEpisode? Episode { get; init; }
}

public sealed record ArrStatusMessage(string? Title, IReadOnlyList<string> Messages);

/// <summary>Film joint à l'entrée de file quand <c>includeMovie=true</c>.</summary>
public sealed record ArrMovie(int Id, int TmdbId, string? ImdbId, int Year, bool HasFile);

/// <summary>Série jointe à l'entrée de file quand <c>includeSeries=true</c>.</summary>
public sealed record ArrSeries(int Id, int TvdbId, int TmdbId, string? ImdbId);

/// <summary>Épisode joint à l'entrée de file quand <c>includeEpisode=true</c>.</summary>
public sealed record ArrEpisode(
    int Id,
    int SeriesId,
    int SeasonNumber,
    int EpisodeNumber,
    bool HasFile);

/// <summary>
/// Un événement d'historique. <b>Source de l'état terminal d'un parcours</b> (ADR-0015) : une
/// entrée qui disparaît de la file peut avoir été importée, supprimée ou échouée, et seule
/// l'historique le dit.
/// </summary>
/// <remarks>
/// La duplication par épisode s'y retrouve : 44 événements pour un pack de saison. Le même
/// regroupement par <see cref="DownloadId"/> s'applique.
/// </remarks>
public sealed record ArrHistoryRecord
{
    public long Id { get; init; }

    public string? DownloadId { get; init; }

    /// <summary>
    /// <c>grabbed</c>, <c>downloadFolderImported</c>, <c>downloadFailed</c>,
    /// <c>downloadIgnored</c>, <c>movieFileDeleted</c>, <c>episodeFileDeleted</c>.
    /// </summary>
    public string? EventType { get; init; }

    public DateTimeOffset Date { get; init; }

    public int? MovieId { get; init; }

    public int? SeriesId { get; init; }

    public int? EpisodeId { get; init; }
}

public sealed record ArrSystemStatus(
    string? Version,
    string? AppName,
    string? InstanceName,
    string? OsName);

public sealed record ArrDiskSpace(
    string? Path,
    string? Label,
    long FreeSpace,
    long TotalSpace);

/// <summary>Message de santé remonté par l'instance elle-même.</summary>
public sealed record ArrHealthCheck(
    string? Source,
    string? Type,
    string? Message,
    [property: JsonPropertyName("wikiUrl")] string? WikiUrl);
