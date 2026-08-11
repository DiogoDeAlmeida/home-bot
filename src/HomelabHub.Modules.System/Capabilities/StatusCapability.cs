using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Abstractions.Modules;

namespace HomelabHub.Modules.SystemInfo.Capabilities;

/// <summary>
/// <c>/system status</c> — version, disponibilité, dernière sauvegarde.
/// </summary>
/// <remarks>
/// Le binding n'a pas de sous-groupe : <c>/system status</c> tient en deux niveaux, et Discord
/// accepte qu'une commande mélange sous-commandes et groupes. Imposer un groupe intermédiaire
/// donnerait <c>/system status show</c>, plus long à taper pour rien.
/// </remarks>
internal sealed class StatusCapability(IModuleState<SystemSnapshot> state) : IHubCapability
{
    public CapabilityDescriptor Descriptor { get; } = new(
        Key: "system.status",
        DisplayName: "État du hub",
        Description: "Version, durée de fonctionnement et dernière sauvegarde.",
        Parameters: [],
        Kind: CapabilityKind.Query,
        Exposure: CapabilityExposure.All,
        Discord: new DiscordBinding(SubGroup: null, Name: "status"));

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
