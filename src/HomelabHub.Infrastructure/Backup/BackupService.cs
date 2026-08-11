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
    ILogger<BackupService> logger) : IHubBackupService, IDisposable
{
    private const string FilePrefix = "homelabhub-";
    private const string FileExtension = ".zip";

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

            using (var stream = File.Create(temporary))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                entries += AddDirectory(archive, platform.DataDirectory, "data",
                                        excluded: platform.BackupsDirectory);
                entries += AddDirectory(archive, platform.ConfigDirectory, "config", excluded: null);
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

    private static int AddDirectory(ZipArchive archive, string source, string prefix, string? excluded)
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
