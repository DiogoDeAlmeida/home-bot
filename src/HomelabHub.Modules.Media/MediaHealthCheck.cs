using HomelabHub.Abstractions.Modules;
using HomelabHub.Modules.Media.Clients;
using HomelabHub.Modules.Media.Correlation;

namespace HomelabHub.Modules.Media;

/// <summary>
/// Sonde de santé : chacun des quatre services répond-il ?
/// </summary>
/// <remarks>
/// C'est ici que <see cref="ServiceResult{T}"/> paie. Une file vide et une instance éteinte
/// produisent la même liste, mais pas la même santé — et sans la distinction, l'interface
/// afficherait sereinement « aucun téléchargement » pendant que Radarr est à l'arrêt.
/// </remarks>
internal sealed class MediaHealthCheck(
    IRadarrClient radarr,
    ISonarrClient sonarr,
    ISeerrClient seerr,
    IQBittorrentClient qbittorrent,
    IModuleState<MediaSnapshot> state) : IModuleHealthCheck
{
    public async Task<ModuleHealth> CheckAsync(CancellationToken cancellationToken)
    {
        var probes = await Task.WhenAll(
            Probe("Radarr", async () => (await radarr.GetSystemStatusAsync(cancellationToken)
                                               .ConfigureAwait(false)).Error),
            Probe("Sonarr", async () => (await sonarr.GetSystemStatusAsync(cancellationToken)
                                               .ConfigureAwait(false)).Error),
            Probe("Seerr", async () => (await seerr.GetStatusAsync(cancellationToken)
                                              .ConfigureAwait(false)).Error),
            Probe("qBittorrent", async () => (await qbittorrent.GetVersionAsync(cancellationToken)
                                                    .ConfigureAwait(false)).Error))
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var down = probes.Count(p => p.State != HealthState.Healthy);

        var (worst, message) = down switch
        {
            0 when state.Current.ObservedAt is null =>
                (HealthState.Unknown, "Services joignables, aucun cycle d'observation encore effectué."),
            0 => (HealthState.Healthy, "Les quatre services répondent."),
            // Un seul service muet dégrade sans arrêter : les autres continuent d'alimenter la
            // corrélation, et la vue reste partiellement juste (convention §14).
            < 4 => (HealthState.Degraded, $"{down} service(s) sur 4 injoignable(s)."),
            _ => (HealthState.Unhealthy, "Aucun service média ne répond."),
        };

        return new ModuleHealth(worst, message, probes, now);
    }

    private static async Task<ServiceHealth> Probe(string name, Func<Task<string?>> check)
    {
        var started = DateTimeOffset.UtcNow;
        var error = await check().ConfigureAwait(false);

        return new ServiceHealth(
            name,
            error is null ? HealthState.Healthy : HealthState.Unhealthy,
            error,
            DateTimeOffset.UtcNow - started);
    }
}
