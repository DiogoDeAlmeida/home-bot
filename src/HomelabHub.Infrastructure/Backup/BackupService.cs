using System.Globalization;
using System.IO.Compression;
using HomelabHub.Core.Backup;
using HomelabHub.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace HomelabHub.Infrastructure.Backup;

/// <summary>
/// Sauvegarde intégrée : une archive unique contenant données, keyring et configuration.
/// </summary>
/// <remarks>
/// <para>
/// L'archive prend <b>tout</b> le répertoire de données, sauvegardes exclues, plus tout le
/// répertoire de configuration. Aucune liste de fichiers à tenir à jour : la base SQLite, quand
/// elle arrivera, sera couverte sans qu'une ligne change ici. Ce qu'on n'a pas à maintenir ne
/// peut pas devenir faux.
/// </para>
/// <para>
/// Le keyring est dans l'archive, obligatoirement et au même endroit que le reste — c'est le
/// point entier d'ADR-0007.
/// </para>
/// </remarks>
internal sealed class BackupService(
    HubPlatform platform,
    HubOptions options,
    IHubConfigStore config,
    ILogger<BackupService> logger,
    Persistence.HubDatabase? database = null) : IHubBackupService, IDisposable
{
    private const string FilePrefix = "homelabhub-";
    private const string FileExtension = ".zip";

    /// <summary>
    /// Fichiers de la base, écartés de la copie brute.
    /// </summary>
    /// <remarks>
    /// En mode WAL, les écritures récentes vivent dans <c>-wal</c> et non dans le fichier
    /// principal. Copier les trois fichiers pendant que le hub tourne produit une archive qui
    /// s'ouvre, se restaure, et a perdu — ou mélangé — les dernières transactions. La base entre
    /// dans l'archive par <c>VACUUM INTO</c>, jamais par <c>File.Copy</c>.
    /// </remarks>
    private static readonly string[] DatabaseFiles =
    [
        Persistence.HubDatabase.FileName,
        Persistence.HubDatabase.FileName + "-wal",
        Persistence.HubDatabase.FileName + "-shm",
    ];

    // Deux sauvegardes simultanées produiraient deux archives partielles du même instant.
    private readonly SemaphoreSlim _lock = new(1, 1);

    public void Dispose() => _lock.Dispose();

    public async Task<BackupArchive> CreateAsync(string reason, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(platform.BackupsDirectory);

            var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var fileName = $"{FilePrefix}{stamp}{FileExtension}";
            var path = Path.Combine(platform.BackupsDirectory, fileName);
            var temporary = path + ".tmp";

            var entries = 0;
            var snapshot = database is null ? null : path + ".db";

            try
            {
                using (var stream = File.Create(temporary))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    entries += AddDirectory(archive, platform.DataDirectory, "data",
                                            excluded: platform.BackupsDirectory,
                                            skipDatabase: database is not null);
                    entries += AddDirectory(archive, platform.ConfigDirectory, "config",
                                            excluded: null, skipDatabase: false);

                    if (snapshot is not null && database is not null)
                    {
                        database.SnapshotTo(snapshot);
                        archive.CreateEntryFromFile(
                            snapshot, $"data/{Persistence.HubDatabase.FileName}",
                            CompressionLevel.Optimal);
                        entries++;
                    }
                }
            }
            finally
            {
                if (snapshot is not null && File.Exists(snapshot))
                {
                    File.Delete(snapshot);
                }
            }

            RestrictPermissions(temporary);
            File.Move(temporary, path, overwrite: true);

            var info = new FileInfo(path);
            var result = new BackupArchive(fileName, info.Length, DateTimeOffset.Now, entries);

            logger.LogInformation(
                "Sauvegarde {File} créée ({Entries} entrées, {Size} octets) — motif : {Reason}.",
                fileName, entries, info.Length, reason);

            ApplyRetention();

            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    public IReadOnlyList<BackupArchive> List()
    {
        if (!Directory.Exists(platform.BackupsDirectory))
        {
            return [];
        }

        return [.. Directory
            .EnumerateFiles(platform.BackupsDirectory, $"{FilePrefix}*{FileExtension}")
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.CreationTimeUtc)
            .Select(info => new BackupArchive(info.Name, info.Length,
                                              new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero),
                                              CountEntries(info.FullName)))];
    }

    /// <summary>
    /// Lit le nombre d'entrées d'une archive existante.
    /// </summary>
    /// <remarks>
    /// Seul le répertoire central du ZIP est parcouru, pas les données : c'est bon marché, et
    /// nettement préférable à afficher « 0 fichiers » pour toute archive listée — un compte nul
    /// est précisément le symptôme d'une sauvegarde qui n'a rien sauvegardé.
    /// </remarks>
    private int CountEntries(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            return archive.Entries.Count;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            logger.LogWarning(ex, "Archive {File} illisible.", Path.GetFileName(path));
            return 0;
        }
    }

    private static int AddDirectory(ZipArchive archive, string source, string prefix,
                                    string? excluded, bool skipDatabase)
    {
        if (!Directory.Exists(source))
        {
            return 0;
        }

        var count = 0;
        var root = Path.GetFullPath(source);
        var skip = excluded is null ? null : Path.GetFullPath(excluded);

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (skip is not null && file.StartsWith(skip, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Un .tmp est une écriture en cours : l'archiver capturerait un état incohérent.
            if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Le verrou de première instance (ADR-0018) est tenu ouvert en exclusivité par CE
            // processus, en continu — précisément le processus qui archive. Tenter de le lire
            // lève : « The process cannot access the file... because it is being used by
            // another process », alors que l'autre processus, c'est lui-même. Il ne porte de
            // toute façon aucune donnée à préserver, seulement un PID et un horodatage.
            if (string.Equals(Path.GetFileName(file), SingleInstanceLock.FileName,
                              StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // La base entre dans l'archive par son instantané, ajouté plus bas.
            if (skipDatabase
                && Array.Exists(DatabaseFiles,
                                name => string.Equals(Path.GetFileName(file), name,
                                                      StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            archive.CreateEntryFromFile(file, $"{prefix}/{relative}", CompressionLevel.Optimal);
            count++;
        }

        return count;
    }

    private void ApplyRetention()
    {
        // Réglage du hub, pas d'un module : la clé vit sous le préfixe réservé « hub. »
        // (ADR-0013). appsettings.json fournit la valeur d'amorçage, l'interface la surcharge.
        var keep = Math.Max(1, config.GetInt32(HubSettings.BackupRetentionKey, options.BackupRetention));

        var stale = Directory
            .EnumerateFiles(platform.BackupsDirectory, $"{FilePrefix}*{FileExtension}")
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.CreationTimeUtc)
            .Skip(keep)
            .ToArray();

        foreach (var file in stale)
        {
            try
            {
                file.Delete();
                logger.LogInformation("Sauvegarde {File} supprimée par la rétention.", file.Name);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Suppression impossible de {File}.", file.Name);
            }
        }
    }

    private static void RestrictPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
