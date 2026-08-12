using HomelabHub.Abstractions.Ingest;
using HomelabHub.Core.Configuration;
using HomelabHub.Core.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HomelabHub.Core.Ingestion;

/// <summary>
/// Fait tourner les pollers de tous les modules : une boucle par poller, pilotée par le noyau.
/// </summary>
/// <remarks>
/// <para>
/// Les modules n'enregistrent pas leurs propres <c>IHostedService</c> : ils y perdraient le
/// contrôle d'activation et le noyau y perdrait la supervision des erreurs. Une exception qui
/// s'échappe d'un cycle est journalisée, le cycle suivant a lieu quand même, et le processus ne
/// tombe jamais.
/// </para>
/// <para>
/// Un module désactivé ou incomplètement configuré voit ses cycles sautés, sans que sa boucle
/// s'arrête : le réactiver depuis l'interface web reprend au cycle suivant, sans redémarrage.
/// </para>
/// </remarks>
internal sealed class ModuleIngestionService(
    ModuleCatalog catalog,
    IModuleRegistry registry,
    IHubConfigStore config,
    RefreshCoordinator refresh,
    Anomalies.AnomalyEngine anomalies,
    IServiceProvider services,
    ILogger<ModuleIngestionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var loops = catalog.Descriptors
            .SelectMany(module => module.Pollers.Select(poller => RunPollerAsync(module, poller, stoppingToken)))
            .ToArray();

        if (loops.Length == 0)
        {
            logger.LogInformation("Aucun poller déclaré.");
            return;
        }

        logger.LogInformation("Démarrage de {Count} poller(s).", loops.Length);
        await Task.WhenAll(loops).ConfigureAwait(false);
    }

    private async Task RunPollerAsync(ModuleDescriptor module, PollerRegistration registration,
                                      CancellationToken stoppingToken)
    {
        var name = registration.PollerType.Name;

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = ResolveInterval(module.Key, registration);

            if (registry.IsActive(module.Key))
            {
                await RunCycleAsync(module, registration, name, stoppingToken).ConfigureAwait(false);
            }

            try
            {
                await refresh.WaitAsync(module.Key, interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunCycleAsync(ModuleDescriptor module, PollerRegistration registration,
                                     string name, CancellationToken stoppingToken)
    {
        // Bornes du cycle (ADR-0005). La réconciliation — « ce qui n'est plus republié est
        // résolu » — n'a lieu que si le cycle se termine sans exception : sinon un service
        // injoignable clôturerait à tort toutes ses alertes, produisant une salve de « tout va
        // bien » au moment précis où quelque chose ne va pas.
        var succeeded = false;
        anomalies.BeginCycle(module.Key);

        try
        {
            var poller = (IModulePoller)services.GetRequiredService(registration.PollerType);
            await poller.PollAsync(stoppingToken).ConfigureAwait(false);
            succeeded = true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            anomalies.CompleteCycle(module.Key, succeeded: false, DateTimeOffset.UtcNow);
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cycle {Poller} du module {Module} en échec.", name, module.Key);
        }

        foreach (var transition in anomalies.CompleteCycle(module.Key, succeeded, DateTimeOffset.UtcNow))
        {
            // Le routage vers un canal de notification viendra avec l'adaptateur ; à ce stade
            // les transitions sont journalisées et lisibles par l'API.
            logger.LogInformation("Anomalie {Kind} : {Key} — {Title}",
                transition.Kind, transition.Anomaly.DedupeKey, transition.Anomaly.Title);
        }

        var succeededLabel = succeeded;
        logger.LogDebug("Cycle {Poller} du module {Module} terminé (succès : {Succeeded}).",
            name, module.Key, succeededLabel);
    }

    private TimeSpan ResolveInterval(string moduleKey, PollerRegistration registration) =>
        registration.IntervalConfigKey is null
            ? registration.DefaultInterval
            : config.GetDuration($"{moduleKey}.{registration.IntervalConfigKey}",
                                 registration.DefaultInterval);
}
