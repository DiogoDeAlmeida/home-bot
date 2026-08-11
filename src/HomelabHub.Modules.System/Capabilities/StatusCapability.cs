using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Abstractions.Modules;

namespace HomelabHub.Modules.SystemInfo.Capabilities;

/// <summary>
/// <c>system status</c> — version et disponibilité.
/// </summary>
/// <remarks>
/// Le chemin de commande n'a qu'un segment : la capacité se rattache directement à la racine du
/// module. Chaque adaptateur le projette dans sa syntaxe — <c>/system status</c> côté Discord.
/// </remarks>
internal sealed class StatusCapability(IModuleState<SystemSnapshot> state) : IHubCapability
{
    public CapabilityDescriptor Descriptor { get; } = new(
        Key: "system.status",
        DisplayName: "État du hub",
        Description: "Version et durée de fonctionnement.",
        Parameters: [],
        Kind: CapabilityKind.Query,
        Exposure: CapabilityExposure.All,
        Command: new CommandBinding("status"));

    public Task<CapabilityResult> ExecuteAsync(CapabilityInvocation invocation,
                                               CancellationToken cancellationToken)
    {
        var snapshot = state.Current;

        if (snapshot.ObservedAt is null)
        {
            return Task.FromResult(CapabilityResult.Ok(
                "Aucune observation depuis le démarrage — le premier cycle n'a pas encore eu lieu.",
                snapshot));
        }

        return Task.FromResult(CapabilityResult.Ok(snapshot));
    }
}
