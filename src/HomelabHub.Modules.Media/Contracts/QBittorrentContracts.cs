using System.Text.Json.Serialization;

namespace HomelabHub.Modules.Media.Contracts;

/// <summary>
/// Modèles de la WebAPI v2 de qBittorrent 5.1.
/// </summary>
/// <remarks>
/// <b>Seule API du lot en snake_case</b>, d'où les attributs explicites : la convention
/// camelCase appliquée aux trois autres services ne fonctionne pas ici.
/// </remarks>
public sealed record QBittorrentTorrent
{
    /// <summary>
    /// Hash du torrent, <b>en minuscules</b>. Radarr et Sonarr le renvoient en majuscules dans
    /// <c>downloadId</c> : c'est la clé de jointure, à normaliser (ADR-0015).
    /// </summary>
    [JsonPropertyName("hash")]
    public string Hash { get; init; } = string.Empty;

    /// <summary>
    /// Hash v1 d'un torrent BitTorrent v2. Identique à <see cref="Hash"/> sur les torrents v1 —
    /// vérifié sur les treize torrents de l'installation. Sert de repli si un torrent v2
    /// apparaît, auquel cas <see cref="Hash"/> pourrait ne plus correspondre au
    /// <c>downloadId</c> des <c>*arr</c>.
    /// </summary>
    [JsonPropertyName("infohash_v1")]
    public string? InfohashV1 { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// <c>downloading</c>, <c>stalledDL</c>, <c>uploading</c>, <c>stalledUP</c>, <c>pausedDL</c>,
    /// <c>pausedUP</c>, <c>checkingDL</c>, <c>error</c>, <c>missingFiles</c>…
    /// </summary>
    [JsonPropertyName("state")]
    public string? State { get; init; }

    /// <summary>Progression de 0 à 1.</summary>
    [JsonPropertyName("progress")]
    public double Progress { get; init; }

    [JsonPropertyName("dlspeed")]
    public long DownloadSpeed { get; init; }

    [JsonPropertyName("upspeed")]
    public long UploadSpeed { get; init; }

    /// <summary>Secondes restantes. 8 640 000 signifie « inconnu » chez qBittorrent.</summary>
    [JsonPropertyName("eta")]
    public long Eta { get; init; }

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("amount_left")]
    public long AmountLeft { get; init; }

    /// <summary>Catégorie posée par le client de téléchargement : <c>radarr</c>, <c>tv-sonarr</c>, ou vide.</summary>
    [JsonPropertyName("category")]
    public string? Category { get; init; }

    [JsonPropertyName("num_seeds")]
    public int NumSeeds { get; init; }

    [JsonPropertyName("num_complete")]
    public int NumComplete { get; init; }

    /// <summary>Disponibilité des pièces. -1 quand qBittorrent ne sait pas.</summary>
    [JsonPropertyName("availability")]
    public double Availability { get; init; }

    [JsonPropertyName("added_on")]
    public long AddedOn { get; init; }

    [JsonPropertyName("last_activity")]
    public long LastActivity { get; init; }

    /// <summary>Clé de jointure normalisée, prête à être comparée à un <c>downloadId</c>.</summary>
    public string JoinKey => (string.IsNullOrEmpty(Hash) ? InfohashV1 ?? string.Empty : Hash)
        .ToLowerInvariant();

    /// <summary>La valeur 8 640 000 signifie « inconnu » et ne doit pas être affichée telle quelle.</summary>
    public TimeSpan? EstimatedTimeLeft =>
        Eta is > 0 and < 8_640_000 ? TimeSpan.FromSeconds(Eta) : null;
}

public sealed record QBittorrentTransferInfo
{
    [JsonPropertyName("dl_info_speed")]
    public long DownloadSpeed { get; init; }

    [JsonPropertyName("up_info_speed")]
    public long UploadSpeed { get; init; }

    [JsonPropertyName("dl_info_data")]
    public long DownloadedBytes { get; init; }

    [JsonPropertyName("up_info_data")]
    public long UploadedBytes { get; init; }

    /// <summary>
    /// <c>connected</c>, <c>firewalled</c>, <c>disconnected</c>. Un <c>disconnected</c>
    /// prolongé est le symptôme d'un tunnel VPN tombé — anomalie explicitement voulue au cadrage.
    /// </summary>
    [JsonPropertyName("connection_status")]
    public string? ConnectionStatus { get; init; }
}
