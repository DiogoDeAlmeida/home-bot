namespace HomelabHub.Core.Anomalies;

/// <summary>
/// Reçoit chaque transition d'anomalie, pour la router vers un canal de notification.
/// </summary>
/// <remarks>
/// <para>
/// Le noyau ne sait notifier personne : aucune implémentation par défaut n'est enregistrée par
/// <c>AddHubCore</c>. Un adaptateur s'inscrit pour être notifié — Discord aujourd'hui, un autre
/// canal demain — sans que le noyau apprenne quoi que ce soit sur lui, exactement comme
/// l'autorisation est tranchée par l'adaptateur et seulement transmise au noyau (ADR-0004,
/// ADR-0016).
/// </para>
/// <para>
/// Appelée pour <b>chaque</b> transition qu'un cycle produit — Opened, Escalated, Reopened,
/// Resolved — jamais pour une simple republication (ADR-0005) : c'est déjà
/// <see cref="AnomalyEngine"/> qui a réduit un flux d'observations répétées à ce petit nombre
/// d'événements. Une notification ratée est journalisée par l'appelant, elle ne doit jamais
/// interrompre le cycle d'ingestion qui l'a produite (convention §14).
/// </para>
/// </remarks>
public interface IAnomalyNotifier
{
    Task NotifyAsync(AnomalyTransition transition, CancellationToken cancellationToken);
}
