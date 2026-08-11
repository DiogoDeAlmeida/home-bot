namespace HomelabHub.Abstractions.Platform;

/// <summary>
/// Services que la plateforme rend aux modules, au même titre que
/// <see cref="Events.IEventPublisher"/> ou <see cref="Modules.IModuleState{TSnapshot}"/>.
/// </summary>
/// <remarks>
/// Ils vivent dans <c>Abstractions</c> parce qu'un module doit pouvoir s'en servir sans
/// référencer le noyau (ADR-0010). Le module <c>system</c> en est le premier client : ses
/// capacités portent justement sur le hub lui-même.
/// </remarks>
public interface IHubPlatform
{
    /// <summary>Version informationnelle du binaire en cours d'exécution.</summary>
    string Version { get; }

    /// <summary>Instant de démarrage du processus.</summary>
    DateTimeOffset StartedAt { get; }

    /// <summary>Répertoire des données persistantes — base, keyring, sauvegardes.</summary>
    string DataDirectory { get; }

    /// <summary>Répertoire de configuration.</summary>
    string ConfigDirectory { get; }
}

/// <summary>Sauvegarde intégrée du hub.</summary>
/// <remarks>
/// <b>L'archive est unique et contient la base, le keyring Data Protection et la
/// configuration</b> (ADR-0007). Ce n'est pas un détail d'implémentation : sur Linux le keyring
/// est un dossier de fichiers en clair, et une base restaurée sans lui rend tous les secrets
/// définitivement illisibles. Produire une archive unique rend l'erreur impossible plutôt que
/// de compter sur la vigilance au moment de la restauration, c'est-à-dire au pire moment.
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
/// <param name="EntryCount">Nombre d'entrées archivées, utile pour repérer une archive vide.</param>
public sealed record BackupArchive(
    string FileName,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    int EntryCount);
