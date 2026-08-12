using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Core.Configuration;

namespace HomelabHub.Core.Events;

/// <summary>
/// Déclenche la rétention du journal et des anomalies résolues sans attendre le prochain
/// passage de <see cref="RetentionService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Capacité du noyau, comme <c>hub.anomaly.snooze</c> : la rétention n'appartient à aucun
/// module. Née d'un besoin concret plutôt que d'une anticipation — vérifier
/// <see cref="RetentionService"/> en conditions réelles sans attendre 24 heures, ni recourir à
/// une horloge truquée que la production ne connaît pas.
/// </para>
/// <para>
/// <b>Sans confirmation</b> : ce qu'elle supprime aurait de toute façon disparu au prochain
/// passage automatique, dans les heures qui suivent au plus. Ce n'est pas une suppression
/// qu'un clic isolé rendrait risquée, seulement une purge normale déclenchée un peu tôt.
/// </para>
/// </remarks>
internal sealed class JournalPurgeCapability(RetentionService retention) : IHubCapability
{
    public CapabilityDescriptor Descriptor { get; } = new(
        Key: $"{HubSettings.Prefix}.journal.purge",
        DisplayName: "Purger maintenant",
        Description: "Applique tout de suite la rétention du journal et des anomalies résolues.",
        Parameters: [],
        Kind: CapabilityKind.Mutation,
        Exposure: CapabilityExposure.All,
        Command: new CommandBinding("journal", "purge"));

    public Task<CapabilityResult> ExecuteAsync(CapabilityInvocation invocation,
                                               CancellationToken cancellationToken)
    {
        var (events, resolved) = retention.Purge();

        return Task.FromResult(CapabilityResult.Ok(
            $"{events} ligne(s) de journal et {resolved} anomalie(s) résolue(s) supprimées."));
    }
}
