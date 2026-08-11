using System.Collections.Concurrent;
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
/// Le tampon est borné et vit en mémoire. L'historique persistant viendra avec la base, avec sa
/// rétention de 14 jours ou 100 000 lignes.
/// </para>
/// </remarks>
internal sealed class HubJournal(ILogger<HubJournal> logger) : IEventPublisher, IHubJournal
{
    private const int Capacity = 500;

    private readonly ConcurrentQueue<HubEvent> _events = new();

    public Task PublishAsync(HubEvent hubEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hubEvent);

        _events.Enqueue(hubEvent);
        while (_events.Count > Capacity && _events.TryDequeue(out _))
        {
            // Tampon glissant.
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
        [.. _events
            .Where(e => minimumSeverity is null || e.Severity >= minimumSeverity)
            .Reverse()
            .Take(Math.Clamp(count, 1, Capacity))];
}
