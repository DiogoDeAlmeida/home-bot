using HomelabHub.Core.Anomalies;
using HomelabHub.Core.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HomelabHub.Core.Events;

/// <summary>
/// Purge quotidienne du journal et des anomalies résolues.
/// </summary>
/// <remarks>
/// <para>
/// Deux bornes, la première atteinte l'emporte : 14 jours ou 100 000 lignes. L'âge seul ne
/// suffit pas — un module bavard peut produire cent mille lignes en deux jours ; le nombre seul
/// ne suffit pas non plus — un hub tranquille garderait des traces d'il y a six mois. Les deux
/// valeurs sont réglables depuis l'interface, sous le préfixe réservé <c>hub.</c> (ADR-0013).
/// </para>
/// <para>
/// Les anomalies <b>résolues</b> suivent la même fenêtre d'âge. Les autres ne sont jamais
/// purgées : une anomalie ouverte depuis trois semaines est exactement ce qu'il faut garder.
/// </para>
/// <para>
/// La purge tourne une fois au démarrage puis à l'intervalle configuré — 24 heures par défaut,
/// réglable via <see cref="HubSettings.JournalPurgeIntervalHoursKey"/> et relu à chaque passage,
/// pas figé au démarrage. Un LXC redémarré tous les jours n'échapperait pas à la rétention, et
/// un LXC qui tourne trois mois ne l'attend pas.
/// </para>
/// <para>
/// <see cref="Purge"/> est aussi accessible directement — voir <see cref="JournalPurgeCapability"/>,
/// qui permet de la déclencher à la demande sans attendre le prochain passage. Utile en
/// exploitation, et c'est exactement ce qui a permis de vérifier ce service en conditions
/// réelles sans attendre 24 heures.
/// </para>
/// </remarks>
internal sealed class RetentionService(
    IJournalStore journal,
    AnomalyEngine anomalies,
    IHubConfigStore config,
    TimeProvider time,
    ILogger<RetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Purge();

            var hours = Math.Max(1, config.GetInt32(HubSettings.JournalPurgeIntervalHoursKey, 24));

            try
            {
                await Task.Delay(TimeSpan.FromHours(hours), time, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Applique la rétention immédiatement, hors de toute attente.</summary>
    /// <returns>Ce qui a été supprimé, pour que l'appelant — journal ou capacité — puisse le dire.</returns>
    public (int Events, int Resolved) Purge()
    {
        try
        {
            var days = Math.Max(1, config.GetInt32(HubSettings.JournalRetentionDaysKey, 14));
            var maximum = Math.Max(1_000, config.GetInt32(HubSettings.JournalMaximumRowsKey, 100_000));
            var cutoff = time.GetUtcNow().AddDays(-days);

            var events = journal.Purge(cutoff, maximum);
            var resolved = anomalies.PurgeResolved(cutoff);

            if (events > 0 || resolved > 0)
            {
                logger.LogInformation(
                    "Rétention appliquée : {Events} ligne(s) de journal et {Resolved} anomalie(s) résolue(s) supprimées.",
                    events, resolved);
            }

            return (events, resolved);
        }
        catch (Exception ex)
        {
            // Une purge ratée n'est pas une raison d'arrêter le hub : la base grossit, et on le
            // saura par ce journal-ci.
            logger.LogError(ex, "Purge de rétention en échec.");
            return (0, 0);
        }
    }
}
