using HomelabHub.Abstractions.Events;
using HomelabHub.Core.Events;

namespace HomelabHub.Core.Tests;

/// <summary>Un magasin de journal en mémoire, dont la purge est pilotable pour les tests.</summary>
internal sealed class RecordingJournalStore : IJournalStore
{
    private readonly List<HubEvent> _events = [];
    private int _removeOnNextPurge;

    public int PurgeCalls { get; private set; }

    public (DateTimeOffset Cutoff, int MaximumRows)? LastPurgeArgs { get; private set; }

    public void Append(HubEvent hubEvent) => _events.Add(hubEvent);

    public IReadOnlyList<HubEvent> Recent(int count, HubEventSeverity? minimumSeverity) => [.. _events];

    public int Purge(DateTimeOffset cutoff, int maximumRows)
    {
        PurgeCalls++;
        LastPurgeArgs = (cutoff, maximumRows);

        var removed = _removeOnNextPurge;
        _removeOnNextPurge = 0;
        return removed;
    }

    /// <summary>Ce que le prochain <see cref="Purge"/> doit prétendre avoir supprimé.</summary>
    public void SetNextPurgeResult(int removed) => _removeOnNextPurge = removed;
}
