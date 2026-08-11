using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Abstractions.Platform;

namespace HomelabHub.Modules.SystemInfo.Capabilities;

/// <summary>
/// Demande une sauvegarde complète du hub.
/// </summary>
/// <remarks>
/// <para>
/// <b>Exposition restreinte au REST, délibérément</b> (ADR-0004). L'archive produite contient le
/// keyring Data Protection, donc de quoi déchiffrer toutes les clés d'API du homelab.
/// </para>
/// <para>
/// <b>Et le module ne pilote pas la sauvegarde : il la demande</b> (ADR-0014). Interdire le
/// déclenchement depuis Discord tout en rendant le service de sauvegarde résoluble par
/// n'importe quel module aurait rouvert l'accès par une autre porte. Ce module n'a donc, comme
/// les autres, qu'un <see cref="IBackupRequester{TModule}"/> : le noyau décide, applique
/// l'anti-rebond, et journalise l'appelant.
/// </para>
/// </remarks>
internal sealed class CreateBackupCapability(IBackupRequester<SystemModule> backups) : IHubCapability
{
    public CapabilityDescriptor Descriptor { get; } = new(
        Key: "system.backup.create",
        DisplayName: "Créer une sauvegarde",
        Description: "Archive la base, le keyring et la configuration dans un fichier unique.",
        Parameters: [],
        Kind: CapabilityKind.Mutation,
        Exposure: CapabilityExposure.Rest,
        Discord: null);

    public async Task<CapabilityResult> ExecuteAsync(CapabilityInvocation invocation,
                                                     CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var reason = invocation.Source == InvocationSource.Internal
            ? "déclenchement automatique"
            : $"demande de {invocation.ActorId}";

        var result = await backups.RequestBackupAsync(reason, cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            BackupRequestOutcome.Created => CapabilityResult.Ok(result.Message),
            BackupRequestOutcome.Throttled => CapabilityResult.Accepted(result.Message),
            _ => CapabilityResult.Fail(result.Message),
        };
    }
}
