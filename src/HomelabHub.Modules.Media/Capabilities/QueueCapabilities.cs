using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Abstractions.Modules;
using HomelabHub.Modules.Media.Correlation;

namespace HomelabHub.Modules.Media.Capabilities;

/// <summary>
/// <c>media queue</c> — le palmarès et le résumé, comme sur le tableau de bord.
/// </summary>
/// <remarks>
/// Renvoie exactement ce que le widget expose. C'est délibéré : une commande qui montrerait
/// autre chose que le tableau de bord obligerait à se demander laquelle des deux a raison.
/// </remarks>
internal sealed class QueueOverviewCapability(IModuleState<MediaSnapshot> state) : IHubCapability
{
    public CapabilityDescriptor Descriptor { get; } = new(
        Key: "media.queue",
        DisplayName: "Téléchargements",
        Description: "Les cinq téléchargements les plus dignes d'attention, et le résumé chiffré.",
        Parameters: [],
        Kind: CapabilityKind.Query,
        Exposure: CapabilityExposure.All,
        Command: new CommandBinding("queue"));

    public Task<CapabilityResult> ExecuteAsync(CapabilityInvocation invocation,
                                               CancellationToken cancellationToken)
    {
        var snapshot = state.Current;

        if (snapshot.ObservedAt is null)
        {
            return Task.FromResult(CapabilityResult.Ok(
                "Aucune observation depuis le démarrage — le premier cycle n'a pas encore eu lieu.",
                MediaOverview.From(snapshot)));
        }

        return Task.FromResult(CapabilityResult.Ok(MediaOverview.From(snapshot)));
    }
}

/// <summary>
/// <c>media queue all</c> — la liste complète, sans troncature.
/// </summary>
/// <remarks>
/// <b>Réservée à l'API.</b> Sur un canal conversationnel, une liste de cinquante parcours est
/// illisible et se ferait de toute façon tronquer par la plateforme. Le cadrage l'avait posé :
/// le détail passe par l'interface, le message permanent reste court.
/// </remarks>
internal sealed class QueueDetailCapability(IModuleState<MediaSnapshot> state) : IHubCapability
{
    public CapabilityDescriptor Descriptor { get; } = new(
        Key: "media.queue.all",
        DisplayName: "Tous les parcours",
        Description: "Liste complète des médias suivis, de la demande à la disponibilité.",
        Parameters: [],
        Kind: CapabilityKind.Query,
        Exposure: CapabilityExposure.Api);

    public Task<CapabilityResult> ExecuteAsync(CapabilityInvocation invocation,
                                               CancellationToken cancellationToken) =>
        Task.FromResult(CapabilityResult.Ok(
            state.Current.Journeys.Select(JourneySummary.From).ToList()));
}
