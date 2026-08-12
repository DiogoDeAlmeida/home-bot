using HomelabHub.Abstractions.Events;
using Microsoft.Extensions.Logging;

namespace HomelabHub.Core.Anomalies;

/// <summary>Table des anomalies et machine à états associée.</summary>
public interface IAnomalyEngine
{
    /// <summary>Anomalies connues, les plus graves et les plus récentes d'abord.</summary>
    IReadOnlyList<Anomaly> All { get; }

    /// <summary>Anomalies actives, c'est-à-dire ouvertes et non mises en sommeil.</summary>
    IReadOnlyList<Anomaly> Active { get; }

    /// <summary>
    /// Met une anomalie en sommeil.
    /// </summary>
    /// <param name="dedupeKey">Anomalie visée.</param>
    /// <param name="until">
    /// Échéance, ou <c>null</c> pour « jusqu'à résolution » — le réarmement n'aura alors lieu
    /// qu'après un passage effectif par l'état résolu.
    /// </param>
    /// <param name="now">Instant courant.</param>
    bool Snooze(string dedupeKey, DateTimeOffset? until, DateTimeOffset now);
}

/// <summary>
/// Transforme un flux d'observations répétées en un petit nombre de transitions.
/// </summary>
/// <remarks>
/// <para>
/// <b>Le cœur d'ADR-0005.</b> Les détecteurs republient l'ensemble de ce qui va mal à chaque
/// cycle — un import bloqué depuis dix heures republie sa clé six cents fois. Le moteur n'en
/// tire qu'une notification à l'ouverture et une à la résolution.
/// </para>
/// <para>
/// <b>La réconciliation n'a lieu qu'après un cycle réussi.</b> Si <c>PollAsync</c> lève à
/// mi-parcours, la moitié des anomalies aurait disparu de l'observation et serait déclarée
/// résolue à tort : un service injoignable produirait une salve de « tout va bien » au lieu
/// d'une alerte. Le cycle abandonné laisse donc la table intacte.
/// </para>
/// <para>
/// <b>La table vit en mémoire.</b> Un redémarrage renotifie les anomalies encore présentes —
/// conséquence assumée tant que SQLite n'est pas là (ADR-0007). C'est le bon sens de l'erreur :
/// renotifier est bruyant, oublier serait dangereux.
/// </para>
/// </remarks>
internal sealed class AnomalyEngine(ILogger<AnomalyEngine> logger) : IAnomalyEngine
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Anomaly> _anomalies = new(StringComparer.Ordinal);

    /// <summary>Observations accumulées pendant le cycle en cours, par module.</summary>
    private readonly Dictionary<string, Dictionary<string, HubEvent>> _openCycles =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<Anomaly> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _anomalies.Values
                    .OrderByDescending(a => a.IsActive)
                    .ThenByDescending(a => a.Severity)
                    .ThenByDescending(a => a.LastSeenAt)];
            }
        }
    }

    public IReadOnlyList<Anomaly> Active
    {
        get
        {
            lock (_gate)
            {
                return [.. _anomalies.Values.Where(a => a.IsActive)
                    .OrderByDescending(a => a.Severity)
                    .ThenBy(a => a.OpenedAt)];
            }
        }
    }

    /// <summary>Ouvre la fenêtre d'observation d'un module.</summary>
    public void BeginCycle(string moduleKey)
    {
        lock (_gate)
        {
            _openCycles[moduleKey] = new Dictionary<string, HubEvent>(StringComparer.Ordinal);
        }
    }

    /// <summary>Enregistre une observation dans le cycle en cours.</summary>
    /// <remarks>
    /// Une observation hors cycle est ignorée pour la réconciliation : sans borne, on ne peut
    /// pas savoir ce qui aurait dû être republié. Elle reste dans le journal.
    /// </remarks>
    public void Observe(string moduleKey, HubEvent anomaly)
    {
        if (string.IsNullOrEmpty(anomaly.DedupeKey))
        {
            return;
        }

        lock (_gate)
        {
            if (_openCycles.TryGetValue(moduleKey, out var cycle))
            {
                cycle[anomaly.DedupeKey] = anomaly;
            }
        }
    }

    /// <summary>
    /// Ferme la fenêtre et applique les transitions.
    /// </summary>
    /// <param name="moduleKey">Module concerné.</param>
    /// <param name="succeeded">
    /// Le cycle s'est-il terminé sans exception ? Sur un échec, rien n'est réconcilié.
    /// </param>
    /// <param name="now">Instant courant.</param>
    public IReadOnlyList<AnomalyTransition> CompleteCycle(string moduleKey, bool succeeded, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_openCycles.Remove(moduleKey, out var observed))
            {
                return [];
            }

            if (!succeeded)
            {
                // Cycle abandonné : l'absence n'est pas significative, la table reste en l'état.
                return [];
            }

            var transitions = new List<AnomalyTransition>();

            foreach (var (key, observation) in observed)
            {
                if (Upsert(key, moduleKey, observation, now) is { } transition)
                {
                    transitions.Add(transition);
                }
            }

            // Ce qui n'a pas été republié par ce module lors d'un cycle réussi est résolu.
            var stale = _anomalies.Values
                .Where(a => string.Equals(a.ModuleKey, moduleKey, StringComparison.OrdinalIgnoreCase))
                .Where(a => a.State != AnomalyState.Resolved)
                .Where(a => !observed.ContainsKey(a.DedupeKey))
                .ToList();

            foreach (var anomaly in stale)
            {
                var resolved = anomaly with { State = AnomalyState.Resolved, ResolvedAt = now };
                _anomalies[anomaly.DedupeKey] = resolved;
                transitions.Add(new AnomalyTransition(AnomalyTransitionKind.Resolved, resolved));

                logger.LogInformation("Anomalie résolue : {Key} après {Duration}.",
                    anomaly.DedupeKey, resolved.Duration);
            }

            return transitions;
        }
    }

    public bool Snooze(string dedupeKey, DateTimeOffset? until, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_anomalies.TryGetValue(dedupeKey, out var anomaly)
                || anomaly.State == AnomalyState.Resolved)
            {
                return false;
            }

            _anomalies[dedupeKey] = anomaly with { State = AnomalyState.Snoozed, SnoozedUntil = until };

            logger.LogInformation("Anomalie {Key} mise en sommeil {Until}.",
                dedupeKey, until?.ToString("u") ?? "jusqu'à résolution");

            return true;
        }
    }

    private AnomalyTransition? Upsert(string key, string moduleKey, HubEvent observation, DateTimeOffset now)
    {
        if (!_anomalies.TryGetValue(key, out var existing) || existing.State == AnomalyState.Resolved)
        {
            // Première apparition, ou réapparition après résolution : nouvelle occurrence.
            // Le compteur repart de zéro, sinon une anomalie récurrente accumulerait un
            // historique qui ne décrirait plus l'épisode en cours.
            var opened = new Anomaly(
                DedupeKey: key,
                ModuleKey: moduleKey,
                Type: observation.Type,
                Severity: observation.Severity,
                Title: observation.Title,
                Body: observation.Body,
                Data: observation.Data,
                State: AnomalyState.Open,
                OpenedAt: now,
                LastSeenAt: now,
                ResolvedAt: null,
                SnoozedUntil: null,
                Occurrences: 1);

            _anomalies[key] = opened;
            logger.LogWarning("Anomalie ouverte : {Key} — {Title}", key, observation.Title);

            return new AnomalyTransition(AnomalyTransitionKind.Opened, opened);
        }

        var escalated = observation.Severity > existing.Severity;
        var expired = existing.State == AnomalyState.Snoozed
                      && existing.SnoozedUntil is { } until
                      && now >= until;

        var updated = existing with
        {
            Severity = observation.Severity,
            Title = observation.Title,
            Body = observation.Body,
            Data = observation.Data,
            LastSeenAt = now,
            Occurrences = existing.Occurrences + 1,
            State = expired ? AnomalyState.Open : existing.State,
            SnoozedUntil = expired ? null : existing.SnoozedUntil,
        };

        _anomalies[key] = updated;

        // Republier n'est pas un événement. Seuls un réveil ou une aggravation le sont.
        if (expired)
        {
            return new AnomalyTransition(AnomalyTransitionKind.Reopened, updated);
        }

        return escalated && updated.State == AnomalyState.Open
            ? new AnomalyTransition(AnomalyTransitionKind.Escalated, updated)
            : null;
    }
}
