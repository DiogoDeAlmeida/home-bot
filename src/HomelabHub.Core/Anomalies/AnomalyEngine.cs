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
/// <b>La table est persistée</b> (ADR-0017). Elle vit en mémoire pour la lecture, et chaque
/// transition est écrite dans <see cref="IAnomalyStore"/>. C'est le seul état que le hub possède :
/// aucun service amont ne sait redire « ouverte depuis dix heures ». Avant la base, un
/// redémarrage rouvrait et renotifiait tout — le bon sens de l'erreur, mais une fois la table
/// durable, il n'y a plus de raison de payer ce bruit.
/// </para>
/// <para>
/// L'écriture a lieu <b>hors du verrou</b>. Le verrou protège une manipulation de dictionnaire,
/// qui se compte en microsecondes ; y enfermer une écriture disque ferait attendre les lectures
/// de l'interface derrière un fsync.
/// </para>
/// </remarks>
internal sealed class AnomalyEngine(IAnomalyStore store, ILogger<AnomalyEngine> logger) : IAnomalyEngine
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

    /// <summary>
    /// Recharge la table depuis le magasin et réconcilie ce qui ne peut plus être republié.
    /// </summary>
    /// <param name="knownModuleKeys">Clés des modules présents dans le catalogue.</param>
    /// <param name="now">Instant courant.</param>
    /// <remarks>
    /// <para>
    /// <b>Le piège de la persistance.</b> Une anomalie ne se referme que parce que son module
    /// cesse de la republier lors d'un cycle réussi. Si le module a disparu du binaire ou a
    /// changé de clé, plus personne ne republiera — et plus personne ne pourra la résoudre. Elle
    /// resterait ouverte pour toujours, visible dans l'interface, sans aucune action possible.
    /// C'est un défaut que la version en mémoire n'avait pas, puisqu'elle repartait de zéro.
    /// </para>
    /// <para>
    /// Ces orphelines sont donc closes au démarrage, sans transition émise : personne ne veut une
    /// salve de « résolu » à chaque mise à jour qui retire un module. La ligne est conservée pour
    /// l'historique, jusqu'à ce que la purge l'emporte.
    /// </para>
    /// <para>
    /// <b>Un module simplement désactivé n'est pas orphelin</b> : sa clé est toujours au
    /// catalogue, ses anomalies l'attendent, et le réactiver reprend exactement où il en était.
    /// </para>
    /// </remarks>
    public void Hydrate(IReadOnlyCollection<string> knownModuleKeys, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(knownModuleKeys);

        var known = new HashSet<string>(knownModuleKeys, StringComparer.OrdinalIgnoreCase);
        var loaded = store.Load();
        var orphans = new List<Anomaly>();

        lock (_gate)
        {
            _anomalies.Clear();

            foreach (var anomaly in loaded)
            {
                if (anomaly.State != AnomalyState.Resolved && !known.Contains(anomaly.ModuleKey))
                {
                    var closed = anomaly with { State = AnomalyState.Resolved, ResolvedAt = now };
                    _anomalies[closed.DedupeKey] = closed;
                    orphans.Add(closed);
                    continue;
                }

                _anomalies[anomaly.DedupeKey] = anomaly;
            }
        }

        if (orphans.Count > 0)
        {
            store.Save(orphans);

            foreach (var orphan in orphans)
            {
                logger.LogWarning(
                    "Anomalie {Key} close d'office : le module {Module} n'est plus au catalogue.",
                    orphan.DedupeKey, orphan.ModuleKey);
            }
        }

        var active = _anomalies.Values.Count(a => a.IsActive);
        logger.LogInformation(
            "Table d'anomalies rechargée : {Total} conservée(s), {Active} active(s), {Orphans} orpheline(s) close(s).",
            loaded.Count, active, orphans.Count);
    }

    /// <summary>Supprime du magasin les anomalies résolues depuis plus longtemps que la fenêtre.</summary>
    /// <remarks>
    /// La table en mémoire est alignée dans la foulée : sans cela, l'interface continuerait
    /// d'afficher un historique que la base ne porte plus, et le redémarrage suivant ferait
    /// disparaître des lignes sans explication.
    /// </remarks>
    public int PurgeResolved(DateTimeOffset cutoff)
    {
        var removed = store.PurgeResolvedBefore(cutoff);

        lock (_gate)
        {
            foreach (var stale in _anomalies.Values
                         .Where(a => a.State == AnomalyState.Resolved
                                     && a.ResolvedAt is { } resolvedAt && resolvedAt < cutoff)
                         .ToList())
            {
                _anomalies.Remove(stale.DedupeKey);
            }
        }

        return removed;
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
        List<AnomalyTransition> transitions;

        // Tout ce que ce cycle a modifié, transition ou non. Une republication sans transition
        // change quand même LastSeenAt et Occurrences : ne pas l'écrire ferait reculer la table
        // au redémarrage, et « vue il y a dix heures » remplacerait « vue il y a trente
        // secondes » sur une anomalie parfaitement vivante.
        List<Anomaly> touched;

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

            transitions = [];
            touched = new List<Anomaly>(observed.Count);

            foreach (var (key, observation) in observed)
            {
                var (transition, current) = Upsert(key, moduleKey, observation, now);
                touched.Add(current);

                if (transition is not null)
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
                touched.Add(resolved);
                transitions.Add(new AnomalyTransition(AnomalyTransitionKind.Resolved, resolved));

                logger.LogInformation("Anomalie résolue : {Key} après {Duration}.",
                    anomaly.DedupeKey, resolved.Duration);
            }
        }

        Persist(touched);

        return transitions;
    }

    public bool Snooze(string dedupeKey, DateTimeOffset? until, DateTimeOffset now)
    {
        Anomaly snoozed;

        lock (_gate)
        {
            if (!_anomalies.TryGetValue(dedupeKey, out var anomaly)
                || anomaly.State == AnomalyState.Resolved)
            {
                return false;
            }

            snoozed = anomaly with { State = AnomalyState.Snoozed, SnoozedUntil = until };
            _anomalies[dedupeKey] = snoozed;
        }

        // Une mise en sommeil non persistée serait annulée par le premier redémarrage, ce qui est
        // exactement le bruit que le sommeil sert à éteindre.
        Persist([snoozed]);

        logger.LogInformation("Anomalie {Key} mise en sommeil {Until}.",
            dedupeKey, until?.ToString("u") ?? "jusqu'à résolution");

        return true;
    }

    /// <summary>
    /// Écrit sans jamais faire tomber le cycle.
    /// </summary>
    /// <remarks>
    /// Une base indisponible dégrade le hub — il oublie à quel moment l'anomalie s'est ouverte —
    /// mais elle ne doit pas arrêter la surveillance : c'est précisément quand la machine va mal
    /// qu'on a besoin qu'elle continue de regarder.
    /// </remarks>
    private void Persist(List<Anomaly> anomalies)
    {
        if (anomalies.Count == 0)
        {
            return;
        }

        try
        {
            store.Save(anomalies);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Persistance de {Count} anomalie(s) impossible.", anomalies.Count);
        }
    }

    private (AnomalyTransition? Transition, Anomaly Current) Upsert(
        string key, string moduleKey, HubEvent observation, DateTimeOffset now)
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

            return (new AnomalyTransition(AnomalyTransitionKind.Opened, opened), opened);
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
            return (new AnomalyTransition(AnomalyTransitionKind.Reopened, updated), updated);
        }

        return escalated && updated.State == AnomalyState.Open
            ? (new AnomalyTransition(AnomalyTransitionKind.Escalated, updated), updated)
            : (null, updated);
    }
}
