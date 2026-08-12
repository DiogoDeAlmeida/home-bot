using HomelabHub.Abstractions.Events;

namespace HomelabHub.Core.Anomalies;

/// <summary>
/// Une condition qui s'ouvre, dure, et se résout.
/// </summary>
/// <remarks>
/// Le noyau en tient la table ; les modules n'en savent rien. Ils republient à chaque cycle
/// l'ensemble de ce qui va mal, et le moteur en déduit les transitions (ADR-0005).
/// </remarks>
/// <param name="DedupeKey">Identité de l'anomalie dans la durée.</param>
/// <param name="ModuleKey">Module émetteur.</param>
/// <param name="Type">Type hiérarchique, pour le filtrage.</param>
/// <param name="Severity">Gravité courante — elle peut évoluer pendant que l'anomalie dure.</param>
/// <param name="Title">Titre courant.</param>
/// <param name="Body">Explication courante.</param>
/// <param name="Data">Données structurées de la dernière observation.</param>
/// <param name="State">Où en est l'anomalie.</param>
/// <param name="OpenedAt">Première observation de cette occurrence.</param>
/// <param name="LastSeenAt">Dernière observation.</param>
/// <param name="ResolvedAt">Instant de la résolution, le cas échéant.</param>
/// <param name="SnoozedUntil">
/// Échéance de la mise en sommeil. <c>null</c> avec <see cref="AnomalyState.Snoozed"/> signifie
/// « jusqu'à résolution ».
/// </param>
/// <param name="Occurrences">Nombre de cycles où l'anomalie a été observée.</param>
public sealed record Anomaly(
    string DedupeKey,
    string ModuleKey,
    string Type,
    HubEventSeverity Severity,
    string Title,
    string? Body,
    IReadOnlyDictionary<string, string>? Data,
    AnomalyState State,
    DateTimeOffset OpenedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? SnoozedUntil,
    int Occurrences)
{
    /// <summary>Depuis combien de temps cette anomalie dure.</summary>
    public TimeSpan Duration => (ResolvedAt ?? LastSeenAt) - OpenedAt;

    /// <summary>Doit-elle apparaître dans les notifications et les listes actives ?</summary>
    public bool IsActive => State is AnomalyState.Open;
}

public enum AnomalyState
{
    /// <summary>Observée au dernier cycle, et signalée.</summary>
    Open = 0,

    /// <summary>Observée, mais volontairement tue jusqu'à son échéance ou sa résolution.</summary>
    Snoozed = 1,

    /// <summary>Plus observée : le module a cessé de la republier lors d'un cycle réussi.</summary>
    Resolved = 2,
}

/// <summary>Ce qui mérite d'être poussé vers un canal de notification.</summary>
/// <param name="Kind">Nature du changement.</param>
/// <param name="Anomaly">État de l'anomalie après la transition.</param>
public sealed record AnomalyTransition(AnomalyTransitionKind Kind, Anomaly Anomaly);

public enum AnomalyTransitionKind
{
    /// <summary>Première apparition.</summary>
    Opened = 0,

    /// <summary>Elle a cessé d'être republiée.</summary>
    Resolved = 1,

    /// <summary>La gravité a augmenté pendant qu'elle durait.</summary>
    Escalated = 2,

    /// <summary>Le sommeil a expiré et l'anomalie est toujours là.</summary>
    Reopened = 3,
}
