namespace HomelabHub.Modules.SystemInfo;

/// <summary>
/// État du hub lui-même. Immuable : c'est ce qui rend l'échange atomique d'ADR-0009 possible.
/// </summary>
/// <remarks>
/// La dernière sauvegarde n'apparaît pas ici : elle relève du noyau, pas d'un module
/// (ADR-0014). Elle est servie par <c>GET /api/backups</c>, au même titre que les réglages du
/// hub sont servis par <c>/api/settings</c>.
/// </remarks>
/// <param name="Version">Version du binaire.</param>
/// <param name="StartedAt">Démarrage du processus.</param>
/// <param name="Uptime">Durée de fonctionnement au moment de l'observation.</param>
/// <param name="Volumes">Occupation des volumes portant les données et la configuration.</param>
/// <param name="ObservedAt">Instant du dernier cycle. <c>null</c> tant qu'aucun n'a eu lieu.</param>
public sealed record SystemSnapshot(
    string Version,
    DateTimeOffset StartedAt,
    TimeSpan Uptime,
    IReadOnlyList<VolumeUsage> Volumes,
    DateTimeOffset? ObservedAt)
{
    /// <summary>
    /// État initial, affichable avant le premier cycle : le dashboard peut le lire dès la
    /// première seconde, et doit y voir « pas encore observé » plutôt que des zéros trompeurs.
    /// </summary>
    public static SystemSnapshot Empty { get; } =
        new("inconnue", DateTimeOffset.MinValue, TimeSpan.Zero, [], null);
}

/// <param name="Label">Nom lisible du volume.</param>
/// <param name="Path">Chemin observé.</param>
/// <param name="TotalBytes">Capacité totale.</param>
/// <param name="FreeBytes">Espace libre.</param>
public sealed record VolumeUsage(string Label, string Path, long TotalBytes, long FreeBytes)
{
    /// <summary>Pourcentage d'espace libre, arrondi au dixième.</summary>
    public double FreePercent =>
        TotalBytes <= 0 ? 0 : Math.Round(100d * FreeBytes / TotalBytes, 1);

    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);
}
