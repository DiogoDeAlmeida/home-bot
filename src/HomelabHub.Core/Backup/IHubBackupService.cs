namespace HomelabHub.Core.Backup;

/// <summary>
/// Pilotage complet de la sauvegarde. <b>Interne au noyau, jamais exposé aux modules</b>
/// (ADR-0014).
/// </summary>
/// <remarks>
/// L'archive contient la base, le keyring Data Protection et la configuration, dans un seul
/// fichier (ADR-0007). Sur Linux le keyring est un dossier de fichiers en clair : une base
/// restaurée sans lui rend tous les secrets définitivement illisibles. L'archive unique rend
/// l'erreur structurellement impossible plutôt que de compter sur la vigilance au moment de la
/// restauration, c'est-à-dire au pire moment.
/// </remarks>
public interface IHubBackupService
{
    /// <summary>Crée une archive et applique la rétention configurée.</summary>
    /// <param name="reason">Motif journalisé : « manuelle », « avant migration », « avant mise à jour ».</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<BackupArchive> CreateAsync(string reason, CancellationToken cancellationToken);

    /// <summary>Archives présentes, les plus récentes d'abord.</summary>
    IReadOnlyList<BackupArchive> List();
}

/// <param name="FileName">Nom du fichier, sans chemin — le chemin absolu ne sort jamais de l'API.</param>
/// <param name="SizeBytes">Taille de l'archive.</param>
/// <param name="CreatedAt">Date de création.</param>
/// <param name="EntryCount">Nombre d'entrées archivées. Une archive à zéro entrée n'a rien sauvegardé.</param>
public sealed record BackupArchive(
    string FileName,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    int EntryCount);
