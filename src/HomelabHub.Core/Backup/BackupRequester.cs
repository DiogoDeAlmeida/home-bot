using HomelabHub.Abstractions.Configuration;
using HomelabHub.Abstractions.Platform;
using HomelabHub.Core.Configuration;
using HomelabHub.Core.Modules;
using Microsoft.Extensions.Logging;

namespace HomelabHub.Core.Backup;

/// <summary>
/// Honore — ou refuse — les demandes de sauvegarde venues des modules.
/// </summary>
/// <remarks>
/// C'est la frontière d'ADR-0014 : un module exprime une intention, le noyau applique la
/// politique. Concrètement, l'anti-rebond empêche qu'un détecteur un peu nerveux ne produise
/// une archive à chaque cycle et ne sature le disque qu'il surveille.
/// </remarks>
internal sealed class BackupRequester<TModule>(
    IHubBackupService backups,
    BackupThrottle throttle,
    ModuleCatalog catalog,
    IHubConfigStore config,
    ILogger<BackupRequester<TModule>> logger) : IBackupRequester<TModule>
    where TModule : IHubModuleMarker
{
    public async Task<BackupRequestResult> RequestBackupAsync(string reason,
                                                              CancellationToken cancellationToken)
    {
        var moduleKey = catalog.GetByType(typeof(TModule)).Key;
        var minimumInterval = config.GetDuration(HubSettings.BackupMinimumIntervalKey,
                                                 TimeSpan.FromMinutes(5));

        if (!throttle.TryAcquire(minimumInterval, out var elapsed))
        {
            logger.LogInformation(
                "Demande de sauvegarde du module {Module} refusée : dernière archive il y a {Elapsed}.",
                moduleKey, elapsed);

            return new BackupRequestResult(BackupRequestOutcome.Throttled,
                $"Une sauvegarde a déjà été faite il y a {FormatElapsed(elapsed)}. " +
                "Réessayer plus tard, ou ajuster l'intervalle minimal dans les paramètres.");
        }

        try
        {
            var archive = await backups.CreateAsync($"{moduleKey} — {reason}", cancellationToken)
                                       .ConfigureAwait(false);

            logger.LogInformation("Sauvegarde demandée par {Module} ({Reason}) : {File}.",
                moduleKey, reason, archive.FileName);

            return new BackupRequestResult(BackupRequestOutcome.Created,
                $"Sauvegarde créée : {archive.FileName} ({archive.EntryCount} fichiers).");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throttle.Release();
            logger.LogError(ex, "Sauvegarde demandée par {Module} en échec.", moduleKey);

            return new BackupRequestResult(BackupRequestOutcome.Failed,
                "La sauvegarde a échoué. Voir le journal pour le détail.");
        }
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed < TimeSpan.FromMinutes(1)
            ? $"{(int)elapsed.TotalSeconds} secondes"
            : $"{(int)elapsed.TotalMinutes} minutes";
}

/// <summary>Anti-rebond partagé par tous les demandeurs.</summary>
/// <remarks>
/// Volontairement global et non par module : ce qui compte est l'espace disque et le coût
/// d'écriture, pas l'identité du demandeur. Trois modules qui demandent chacun une sauvegarde
/// dans la même minute n'en justifient qu'une.
/// </remarks>
internal sealed class BackupThrottle(TimeProvider time)
{
    private readonly Lock _gate = new();
    private DateTimeOffset? _last;

    public bool TryAcquire(TimeSpan minimumInterval, out TimeSpan elapsedSinceLast)
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();

            if (_last is { } last && now - last < minimumInterval)
            {
                elapsedSinceLast = now - last;
                return false;
            }

            elapsedSinceLast = TimeSpan.Zero;
            _last = now;
            return true;
        }
    }

    /// <summary>Rend le jeton après un échec : une sauvegarde ratée ne doit pas bloquer la suivante.</summary>
    public void Release()
    {
        lock (_gate)
        {
            _last = null;
        }
    }
}
