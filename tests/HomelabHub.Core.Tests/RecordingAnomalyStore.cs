using HomelabHub.Core.Anomalies;

namespace HomelabHub.Core.Tests;

/// <summary>
/// Un magasin d'anomalies en mémoire, qui se comporte comme la base sans en être une.
/// </summary>
/// <remarks>
/// Il conserve les lignes entre deux moteurs, ce qui est exactement ce qu'il faut pour rejouer
/// un redémarrage : on construit un moteur, on le nourrit, on en construit un second sur le
/// même magasin, et on vérifie qu'il n'a rien oublié ni rien réinventé.
/// </remarks>
internal sealed class RecordingAnomalyStore : IAnomalyStore
{
    private readonly Dictionary<string, Anomaly> _rows = new(StringComparer.Ordinal);

    /// <summary>Nombre d'appels à <see cref="Save"/>, pour vérifier qu'on écrit sans excès.</summary>
    public int Writes { get; private set; }

    public IReadOnlyList<Anomaly> Load() => [.. _rows.Values];

    public void Save(IReadOnlyList<Anomaly> anomalies)
    {
        Writes++;

        foreach (var anomaly in anomalies)
        {
            _rows[anomaly.DedupeKey] = anomaly;
        }
    }

    public int PurgeResolvedBefore(DateTimeOffset cutoff)
    {
        var stale = _rows.Values
            .Where(a => a.State == AnomalyState.Resolved
                        && a.ResolvedAt is { } resolvedAt && resolvedAt < cutoff)
            .ToList();

        foreach (var anomaly in stale)
        {
            _rows.Remove(anomaly.DedupeKey);
        }

        return stale.Count;
    }
}
