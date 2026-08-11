using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Abstractions.Modules;

namespace HomelabHub.Modules.SystemInfo.Capabilities;

/// <summary><c>/system disk</c> — occupation des volumes.</summary>
internal sealed class DiskCapability(IModuleState<SystemSnapshot> state) : IHubCapability
{
    public CapabilityDescriptor Descriptor { get; } = new(
        Key: "system.disk",
        DisplayName: "Espace disque",
        Description: "Occupation des volumes portant les données et la configuration.",
        Parameters: [],
        Kind: CapabilityKind.Query,
        Exposure: CapabilityExposure.All,
        Discord: new DiscordBinding(SubGroup: null, Name: "disk"));

    public Task<CapabilityResult> ExecuteAsync(CapabilityInvocation invocation,
                                               CancellationToken cancellationToken) =>
        Task.FromResult(CapabilityResult.Ok(state.Current.Volumes));
}
