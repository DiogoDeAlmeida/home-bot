using System.Collections.Concurrent;
using HomelabHub.Abstractions.Events;

namespace HomelabHub.Core.Events;

/// <summary>Où vont les événements publiés.</summary>
/// <remarks>
/// Deux implémentations : un tampon glissant en mémoire, suffisant pour les tests et pour un hub
/// sans base, et un magasin SQLite avec rétention. Le journal lui-même ne connaît ni l'un ni
/// l'autre.
/// </remarks>
public interface IJournalStore
{
    void Append(HubEvent hubEvent);

    IReadOnlyList<HubEvent> Recent(int count, HubEventSeverity? minimumSeverity);

    /// <summary>
    /// Applique la rétention : ce qui est plus vieux que <paramref name="cutoff"/>, et ce qui
    /// dépasse <paramref name="maximumRows"/> lignes une fois l'âge appliqué.
    /// </summary>
    /// <returns>Nombre de lignes supprimées.</returns>
    int Purge(DateTimeOffset cutoff, int maximumRows);
}

/// <summary>Tampon glissant borné, sans persistance.</summary>
internal sealed class InMemoryJournalStore : IJournalStore
{
    private const int Capacity = 500;

    private readonly ConcurrentQueue<HubEvent> _events = new();

    public void Append(HubEvent hubEvent)
    {
        _events.Enqueue(hubEvent);
        while (_events.Count > Capacity && _events.TryDequeue(out _))
        {
            // Tampon glissant.
        }
    }

    public IReadOnlyList<HubEvent> Recent(int count, HubEventSeverity? minimumSeverity) =>
        [.. _events
            .Where(e => minimumSeverity is null || e.Severity >= minimumSeverity)
            .Reverse()
            .Take(Math.Clamp(count, 1, Capacity))];

    /// <summary>Sans objet : la capacité est déjà la rétention.</summary>
    public int Purge(DateTimeOffset cutoff, int maximumRows) => 0;
}
