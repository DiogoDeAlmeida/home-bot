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
    IModuleState<SystemSnapshot> state,
    IModuleConfiguration<SystemModule> config,
    IEventPublisher events,
    IGitHubReleaseClient releases) : IModulePoller
{
    public async Task PollAsync(CancellationToken cancellationToken)
    {
        var volumes = ReadVolumes();
        var now = DateTimeOffset.UtcNow;
        var previous = state.Current;

        var (latestVersion, checkedAt) = await ResolveLatestVersionAsync(previous, now, cancellationToken)
            .ConfigureAwait(false);

        state.Mutate(_ => new SystemSnapshot(
            platform.Version,
            platform.StartedAt,
            now - platform.StartedAt,
            volumes,
            now,
            config.GetInt32(SystemModule.WarnBelowPercentKey, 15),
            config.GetInt32(SystemModule.CriticalBelowPercentKey, 7),
            latestVersion,
            checkedAt));

        await PublishDiskAnomaliesAsync(volumes, now, cancellationToken).ConfigureAwait(false);
        await PublishUpdateAnomalyAsync(latestVersion, now, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// N'interroge GitHub que si l'intervalle configuré est écoulé — le poller lui-même tourne
    /// bien plus souvent que ça ne devrait être nécessaire de vérifier (voir
    /// <see cref="SystemModule.UpdateCheckIntervalHoursKey"/>). Entre deux vérifications, la
    /// dernière valeur connue est conservée : un GitHub momentanément injoignable ne doit pas
    /// faire disparaître un signal déjà vu.
    /// </summary>
    private async Task<(string? LatestVersion, DateTimeOffset? CheckedAt)> ResolveLatestVersionAsync(
        SystemSnapshot previous, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var interval = config.GetDuration(SystemModule.UpdateCheckIntervalHoursKey, TimeSpan.FromHours(12));
        var due = previous.UpdateCheckedAt is not { } lastChecked || now - lastChecked >= interval;

        if (!due)
        {
            return (previous.LatestAvailableVersion, previous.UpdateCheckedAt);
        }

        var tag = await releases.GetLatestReleaseTagAsync(cancellationToken).ConfigureAwait(false);

        // L'horodatage avance même en cas d'échec : sinon un dépôt injoignable ferait retenter
        // à chaque cycle du poller (par défaut chaque minute) plutôt qu'à la cadence voulue.
        return (tag ?? previous.LatestAvailableVersion, now);
    }

    /// <summary>
    /// Republie « nouvelle version disponible » tant qu'elle l'est — un détecteur republie ce
    /// qui va toujours, pas seulement ce qui vient de changer (ADR-0005). L'anomalie se referme
    /// d'elle-même une fois le binaire mis à jour, sans action de ce poller.
    /// </summary>
    private async Task PublishUpdateAnomalyAsync(string? latestVersion, DateTimeOffset now,
                                                  CancellationToken cancellationToken)
    {
        if (!IsNewer(latestVersion, platform.Version))
        {
            return;
        }

        await events.PublishAsync(new HubEvent(
            ModuleKey: "system",
            Type: "system.update.available",
            // Un signalement, jamais une panne : Warning notifie une fois à l'ouverture puis se
            // tait jusqu'à la mise à jour manuelle ou la résolution (ADR-0005), sans jamais
            // rien déclencher lui-même.
            Severity: HubEventSeverity.Warning,
            Title: $"Nouvelle version disponible : {latestVersion}",
            Body: $"Version installée : {platform.Version}. La mise à jour reste manuelle — " +
                  $"voir docs/03-deploiement.md.",
            DedupeKey: "system.update.available",
            Data: new Dictionary<string, string>
            {
                ["current"] = platform.Version,
                ["latest"] = latestVersion!,
            },
            OccurredAt: now), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Compare deux étiquettes semver, préfixe <c>v</c> optionnel. Silencieux par défaut : une
    /// étiquette illisible ne doit jamais déclencher une fausse alerte.
    /// </summary>
    private static bool IsNewer(string? latestTag, string currentVersion)
    {
        if (string.IsNullOrWhiteSpace(latestTag))
        {
            return false;
        }

        var latestText = latestTag.StartsWith('v') || latestTag.StartsWith('V')
            ? latestTag[1..]
            : latestTag;

        return Version.TryParse(latestText, out var latest)
               && Version.TryParse(currentVersion, out var current)
               && latest > current;
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
        var warn = config.GetInt32(SystemModule.WarnBelowPercentKey, 15);
        var critical = config.GetInt32(SystemModule.CriticalBelowPercentKey, 7);

        foreach (var volume in volumes.Where(v => v.TotalBytes > 0 && v.FreePercent < warn))
        {
            await events.PublishAsync(new HubEvent(
                ModuleKey: "system",
                Type: "system.disk.low",
                // Deux seuils indépendants plutôt qu'un seuil et sa moitié : la gravité d'un
                // disque plein ne se déduit pas d'une règle de trois, elle dépend de la vitesse
                // à laquelle il se remplit — donc de l'usage, donc du réglage.
                Severity: volume.FreePercent < critical
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
