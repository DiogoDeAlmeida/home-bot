using HomelabHub.Abstractions.Configuration;
using HomelabHub.Abstractions.Modules;

namespace HomelabHub.Modules.SystemInfo;

/// <summary>
/// Santé du hub lui-même. N'interroge rien de distant : elle relit le snapshot.
/// </summary>
/// <remarks>
/// Elle vit hors d'<see cref="IHubModule"/> parce qu'elle a besoin de services résolus par le
/// conteneur, lequel n'existe pas encore quand le module est instancié (ADR-0002).
/// </remarks>
internal sealed class SystemHealthCheck(
    IModuleState<SystemSnapshot> state,
    IModuleConfiguration<SystemModule> config) : IModuleHealthCheck
{
    public Task<ModuleHealth> CheckAsync(CancellationToken cancellationToken)
    {
        var snapshot = state.Current;
        var now = DateTimeOffset.UtcNow;

        if (snapshot.ObservedAt is null)
        {
            return Task.FromResult(new ModuleHealth(
                HealthState.Unknown, "Aucune observation depuis le démarrage.", [], now));
        }

        var warn = config.GetInt32(SystemModule.WarnBelowPercentKey, 15);
        var critical = config.GetInt32(SystemModule.CriticalBelowPercentKey, 7);

        var services = snapshot.Volumes
            .Select(volume => new ServiceHealth(
                volume.Label,
                volume.FreePercent switch
                {
                    var p when p < critical => HealthState.Unhealthy,
                    var p when p < warn => HealthState.Degraded,
                    _ => HealthState.Healthy,
                },
                $"{SystemPoller.FormatBytes(volume.FreeBytes)} libres sur " +
                $"{SystemPoller.FormatBytes(volume.TotalBytes)}."))
            .ToArray();

        var worst = services.Length == 0
            ? HealthState.Unknown
            : services.Max(s => s.State);

        var message = worst switch
        {
            HealthState.Healthy => "Espace disque suffisant.",
            HealthState.Degraded => $"Au moins un volume passe sous {warn} % d'espace libre.",
            HealthState.Unhealthy => $"Au moins un volume passe sous {critical} % d'espace libre.",
            _ => "Aucun volume lisible.",
        };

        return Task.FromResult(new ModuleHealth(worst, message, services, now));
    }
}
