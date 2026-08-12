using HomelabHub.Abstractions.Events;
using Microsoft.Extensions.Logging;

namespace HomelabHub.Core.Events;

/// <summary>Derniers événements publiés, consultables depuis l'interface sans SSH.</summary>
public interface IHubJournal
{
    /// <summary>Événements les plus récents d'abord.</summary>
    IReadOnlyList<HubEvent> Recent(int count = 100, HubEventSeverity? minimumSeverity = null);
}

/// <summary>
/// Publication des événements : journalisation structurée et tampon consultable.
/// </summary>
/// <remarks>
/// <para>
/// La déduplication par <c>DedupeKey</c>, la mise en sommeil et le routage vers Discord
/// arrivent à l'étape 4 avec le moteur d'anomalies (ADR-0005). Ce qui est en place ici est ce
/// qui sert dès maintenant : voir ce qui se passe.
/// </para>
/// <para>
/// Le stockage est délégué à <see cref="IJournalStore"/> : tampon glissant en mémoire par
/// défaut, table SQLite avec rétention — 14 jours ou 100 000 lignes — quand la base est là.
/// </para>
/// </remarks>
internal sealed class HubJournal(
    Anomalies.AnomalyEngine anomalies,
    IJournalStore store,
    ILogger<HubJournal> logger) : IEventPublisher, IHubJournal
{
    public Task PublishAsync(HubEvent hubEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hubEvent);

        // Un événement porteur d'une clé de déduplication est une anomalie : le moteur en tient
        // l'état et n'en tirera qu'une notification à l'ouverture et une à la résolution. Les
        // autres sont des faits ponctuels, journalisés et rien de plus (ADR-0005).
        anomalies.Observe(hubEvent.ModuleKey, hubEvent);

        try
        {
            store.Append(hubEvent);
        }
        catch (Exception ex)
        {
            // Le contrat d'IEventPublisher est de ne jamais lever : un journal en panne ne doit
            // pas interrompre l'ingestion qui l'alimente.
            logger.LogError(ex, "Écriture du journal impossible pour {Type}.", hubEvent.Type);
        }

        var level = hubEvent.Severity switch
        {
            HubEventSeverity.Critical => LogLevel.Error,
            HubEventSeverity.Warning => LogLevel.Warning,
            _ => LogLevel.Information,
        };

#pragma warning disable CA2254 // Le gabarit est constant ; seuls les arguments varient.
        logger.Log(level, "[{Module}] {Type} — {Title}", hubEvent.ModuleKey, hubEvent.Type, hubEvent.Title);
#pragma warning restore CA2254

        return Task.CompletedTask;
    }

    public IReadOnlyList<HubEvent> Recent(int count = 100, HubEventSeverity? minimumSeverity = null) =>
        store.Recent(count, minimumSeverity);
}
