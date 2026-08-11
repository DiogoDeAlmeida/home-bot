namespace HomelabHub.Modules.Media.Contracts;

/// <summary>Modèles de l'API v1 de Seerr 3.4.1 (anciennement Jellyseerr).</summary>
public sealed record SeerrPage<T>(SeerrPageInfo PageInfo, IReadOnlyList<T> Results);

public sealed record SeerrPageInfo(int Pages, int PageSize, int Results, int Page);

/// <summary>Une demande de média.</summary>
/// <param name="Id">Identifiant de la requête.</param>
/// <param name="Status">1 = en attente d'approbation, 2 = approuvée, 3 = refusée.</param>
/// <param name="Type">« movie » ou « tv ».</param>
/// <param name="CreatedAt">Date de la demande.</param>
/// <param name="Media">Média demandé, porteur des clés de jointure.</param>
/// <param name="Seasons">
/// Saisons demandées. Une requête peut en porter plusieurs — trois dans les données observées.
/// </param>
public sealed record SeerrRequest(
    int Id,
    int Status,
    string? Type,
    DateTimeOffset CreatedAt,
    SeerrMedia? Media,
    IReadOnlyList<SeerrSeason> Seasons);

/// <summary>
/// Média associé à une requête.
/// </summary>
/// <remarks>
/// <see cref="ExternalServiceId"/> est <b>la clé de jointure amont</b> : il vaut l'identifiant
/// interne du film côté Radarr (<c>movieId</c>) ou de la série côté Sonarr (<c>seriesId</c>).
/// Vérifié sur données réelles : requête #77 → <c>seriesId=21</c>, requête #76 → <c>movieId=49</c>.
/// </remarks>
public sealed record SeerrMedia
{
    public int Id { get; init; }

    /// <summary>« movie » ou « tv » — détermine s'il faut joindre Radarr ou Sonarr.</summary>
    public string? MediaType { get; init; }

    public int? TmdbId { get; init; }

    public int? TvdbId { get; init; }

    public string? ImdbId { get; init; }

    /// <summary>1 inconnu, 2 en attente, 3 en traitement, 4 partiellement disponible, 5 disponible.</summary>
    public int Status { get; init; }

    /// <summary>Identifiant interne côté Radarr ou Sonarr. Clé de jointure amont.</summary>
    public int? ExternalServiceId { get; init; }

    /// <summary>Quelle instance <c>*arr</c>, quand plusieurs sont configurées.</summary>
    public int? ServiceId { get; init; }

    public DateTimeOffset? MediaAddedAt { get; init; }

    /// <summary>
    /// Vue de téléchargement corrélée par Seerr lui-même.
    /// </summary>
    /// <remarks>
    /// <b>Délibérément non utilisée comme source de vérité</b> (ADR-0015). Elle porte le même
    /// défaut de duplication que la file — dix entrées pour un seul titre — et elle diverge de
    /// Sonarr : saison 1 en <c>warning</c> chez Seerr pendant que Sonarr téléchargeait la
    /// saison 2. Seul son <c>downloadId</c> sert, comme raccourci de jointure.
    /// </remarks>
    public IReadOnlyList<SeerrDownloadStatus> DownloadStatus { get; init; } = [];
}

public sealed record SeerrSeason(int Id, int SeasonNumber, int Status);

/// <param name="DownloadId">Hash du torrent — le seul champ de ce type qui serve.</param>
/// <param name="ExternalId">Identifiant côté service externe.</param>
/// <param name="Status">Statut rapporté par Seerr, non fiable.</param>
/// <param name="Size">Taille annoncée.</param>
/// <param name="SizeLeft">Reste annoncé.</param>
public sealed record SeerrDownloadStatus(
    string? DownloadId,
    int? ExternalId,
    string? Status,
    long Size,
    long SizeLeft);

public sealed record SeerrStatus(string? Version, string? CommitTag, bool UpdateAvailable);
