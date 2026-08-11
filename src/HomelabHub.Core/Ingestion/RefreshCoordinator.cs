using System.Collections.Concurrent;

namespace HomelabHub.Core.Ingestion;

/// <summary>
/// Permet de réveiller les pollers d'un module sans attendre la fin de leur intervalle.
/// </summary>
/// <remarks>
/// C'est ce qui rend le couple push/poll intéressant (ADR-0003) : un webhook renvoyant
/// <c>RequestRefresh</c> déclenche un cycle immédiat, ce qui autorise un intervalle sobre sans
/// latence perçue. Le sémaphore borné à 1 absorbe naturellement les rafales — dix webhooks en
/// deux secondes ne provoquent pas dix cycles.
/// </remarks>
public interface IRefreshCoordinator
{
    /// <summary>Demande un cycle anticipé pour tous les pollers d'un module.</summary>
    void Request(string moduleKey);
}

internal sealed class RefreshCoordinator : IRefreshCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _signals =
        new(StringComparer.OrdinalIgnoreCase);

    public void Request(string moduleKey)
    {
        var signal = SignalFor(moduleKey);

        // Ne jamais dépasser 1 : plusieurs demandes rapprochées valent une seule relance.
        if (signal.CurrentCount == 0)
        {
            try
            {
                signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Course bénigne entre deux demandes simultanées.
            }
        }
    }

    /// <summary>
    /// Attend soit une demande de rafraîchissement, soit l'écoulement de l'intervalle.
    /// </summary>
    /// <returns><c>true</c> si un rafraîchissement a été demandé, <c>false</c> si l'intervalle a expiré.</returns>
    internal Task<bool> WaitAsync(string moduleKey, TimeSpan interval, CancellationToken cancellationToken) =>
        SignalFor(moduleKey).WaitAsync(interval, cancellationToken);

    private SemaphoreSlim SignalFor(string moduleKey) =>
        _signals.GetOrAdd(moduleKey, static _ => new SemaphoreSlim(0, 1));
}
