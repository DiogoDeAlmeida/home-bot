namespace HomelabHub.Abstractions.Events;

/// <summary>Publie un événement vers le noyau, qui décide de sa déduplication et de son routage.</summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publie un événement. Ne lève jamais : un échec de notification ne doit pas interrompre
    /// une ingestion en cours.
    /// </summary>
    Task PublishAsync(HubEvent hubEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Un fait signalé par un module.
/// </summary>
/// <remarks>
/// <b>Une anomalie est un état, pas un événement (ADR-0005).</b> « Téléchargement bloqué
/// depuis quatre heures » traité comme un événement produit deux cent quarante messages
/// Discord. Le noyau maintient donc une table d'anomalies actives, indexée par
/// <see cref="DedupeKey"/>, et ne notifie qu'aux transitions : ouverture, mise en sommeil,
/// résolution. Republier le même <see cref="DedupeKey"/> à chaque cycle est le comportement
/// attendu — c'est ainsi que le noyau sait que l'anomalie dure toujours.
/// </remarks>
/// <param name="ModuleKey">Module émetteur.</param>
/// <param name="Type">
/// Type hiérarchique et stable : <c>media.download.stalled</c>,
/// <c>media.import.manual-required</c>, <c>system.disk.low</c>. Sert de clé de routage et
/// de filtre dans le journal.
/// </param>
/// <param name="Severity">Gravité.</param>
/// <param name="Title">Titre court, en français.</param>
/// <param name="Body">Détail, en français.</param>
/// <param name="DedupeKey">
/// Identité de l'anomalie dans la durée — typiquement le hash du torrent concerné, ou
/// l'identifiant du média. <c>null</c> pour un fait ponctuel, notifié une fois et archivé.
/// </param>
/// <param name="Data">Données structurées, pour le rendu et le filtrage. Jamais de secret.</param>
/// <param name="OccurredAt">Horodatage.</param>
public sealed record HubEvent(
    string ModuleKey,
    string Type,
    HubEventSeverity Severity,
    string Title,
    string? Body,
    string? DedupeKey,
    IReadOnlyDictionary<string, string>? Data,
    DateTimeOffset OccurredAt);

public enum HubEventSeverity
{
    /// <summary>Information : consultable dans le journal, jamais poussée dans Discord.</summary>
    Info = 0,

    /// <summary>Anomalie : notifiée à l'ouverture et à la résolution.</summary>
    Warning = 1,

    /// <summary>Anomalie bloquante : tunnel VPN tombé, disque plein, service injoignable.</summary>
    Critical = 2,
}
