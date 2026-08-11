using System.Globalization;
using HomelabHub.Abstractions.Configuration;
using HomelabHub.Abstractions.Events;
using HomelabHub.Abstractions.Ingest;
using HomelabHub.Abstractions.Modules;
using HomelabHub.Abstractions.Platform;

namespace HomelabHub.Modules.SystemInfo;

/// <summary>Observe le hub et projette le résultat dans le snapshot.</summary>
internal sealed class SystemPoller(
    IHubPlatform platform,
    IHubBackupService backups,
    IModuleState<SystemSnapshot> state,
    IModuleConfiguration<SystemModule> config,
    IEventPublisher events) : IModulePoller
{
    public async Task PollAsync(CancellationToken cancellationToken)
    {
        var volumes = ReadVolumes();
        var archives = backups.List();
        var lastBackup = archives.Count > 0 ? archives[0] : null;
        var now = DateTimeOffset.UtcNow;

        state.Mutate(_ => new SystemSnapshot(
            platform.Version,
            platform.StartedAt,
            now - platform.StartedAt,
            volumes,
            lastBackup,
            now));

        await PublishDiskAnomaliesAsync(volumes, now, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Republie l'ensemble des volumes sous seuil, à chaque cycle.
    /// </summary>
    /// <remarks>
    /// Un détecteur est une projection sans état, jamais un émetteur (ADR-0005) : il ne dit pas
    /// « ferme cette alerte », il dit « voici tout ce qui ne va pas maintenant ». Un volume qui
    /// repasse au-dessus du seuil cesse simplement d'apparaître, et le noyau en déduit la
    /// clôture. Rien à mémoriser ici.
    /// </remarks>
    private async Task PublishDiskAnomaliesAsync(IReadOnlyList<VolumeUsage> volumes,
                                                 DateTimeOffset now,
                                                 CancellationToken cancellationToken)
    {
        var threshold = config.GetInt32(SystemModule.FreeSpaceThresholdKey, 10);

        foreach (var volume in volumes.Where(v => v.TotalBytes > 0 && v.FreePercent < threshold))
        {
            await events.PublishAsync(new HubEvent(
                ModuleKey: "system",
                Type: "system.disk.low",
                Severity: volume.FreePercent < threshold / 2d
                    ? HubEventSeverity.Critical
                    : HubEventSeverity.Warning,
                Title: $"Espace disque faible sur {volume.Label}",
                Body: $"{volume.FreePercent.ToString("0.#", CultureInfo.GetCultureInfo("fr-FR"))} % libres " +
                      $"({FormatBytes(volume.FreeBytes)} sur {FormatBytes(volume.TotalBytes)}).",
                DedupeKey: $"system.disk.low:{volume.Path}",
                Data: new Dictionary<string, string>
                {
                    ["path"] = volume.Path,
                    ["freePercent"] = volume.FreePercent.ToString(CultureInfo.InvariantCulture),
                },
                OccurredAt: now), cancellationToken).ConfigureAwait(false);
        }
    }

    private List<VolumeUsage> ReadVolumes()
    {
        var volumes = new List<VolumeUsage>();

        // Données et configuration peuvent vivre sur deux volumes différents (/opt et /etc).
        // On observe les deux, dédupliqués par point de montage.
        foreach (var (label, path) in new[]
                 {
                     ("Données", platform.DataDirectory),
                     ("Configuration", platform.ConfigDirectory),
                 })
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path)) ?? path);
                if (!drive.IsReady)
                {
                    continue;
                }

                if (volumes.Exists(v => string.Equals(v.Path, drive.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                volumes.Add(new VolumeUsage(label, drive.Name, drive.TotalSize, drive.AvailableFreeSpace));
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                // Convention §14 : un volume illisible dégrade l'affichage, il n'interrompt pas
                // le cycle — les autres volumes doivent quand même être observés.
            }
        }

        return volumes;
    }

    internal static string FormatBytes(long bytes)
    {
        string[] units = ["o", "Ko", "Mo", "Go", "To"];
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.GetCultureInfo("fr-FR"), $"{value:0.#} {units[unit]}");
    }
}
