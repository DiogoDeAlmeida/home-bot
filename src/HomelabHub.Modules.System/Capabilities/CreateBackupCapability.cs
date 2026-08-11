using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Abstractions.Platform;

namespace HomelabHub.Modules.SystemInfo.Capabilities;

/// <summary>
/// Déclenche une sauvegarde complète du hub.
/// </summary>
/// <remarks>
/// <para>
/// <b>Exposition restreinte au REST, délibérément</b> (ADR-0004). L'archive produite contient le
/// keyring Data Protection, donc de quoi déchiffrer toutes les clés d'API du homelab. Elle ne
/// doit jamais être déclenchable depuis Discord, quel que soit le rôle de l'appelant — un
/// message éphémère reste un message, et l'archive resterait sur le disque du hub. Elle reste
/// derrière l'authentification admin de l'interface web.
/// </para>
/// <para>
/// <see cref="CapabilityExposure"/> est indépendant de <see cref="CapabilityKind"/> pour
/// exactement ce cas : <c>Mutation</c> dit <i>qui</i> peut appeler, <c>Exposure</c> dit
/// <i>d'où</i>. Le validateur de démarrage refuse qu'un <c>DiscordBinding</c> soit déclaré ici.
/// </para>
/// </remarks>
internal sealed class CreateBackupCapability(IHubBackupService backups) : IHubCapability
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
            ? "automatique"
            : $"manuelle ({invocation.ActorId})";

        var archive = await backups.CreateAsync(reason, cancellationToken).ConfigureAwait(false);

        return CapabilityResult.Ok(
            $"Sauvegarde créée : {archive.FileName} " +
            $"({archive.EntryCount} fichiers, {SystemPoller.FormatBytes(archive.SizeBytes)}).",
            archive);
    }
}

/// <summary>Liste les archives présentes. Restreinte au REST pour la même raison.</summary>
internal sealed class ListBackupsCapability(IHubBackupService backups) : IHubCapability
{
    public CapabilityDescriptor Descriptor { get; } = new(
        Key: "system.backup.list",
        DisplayName: "Sauvegardes",
        Description: "Archives présentes, les plus récentes d'abord.",
        Parameters: [],
        Kind: CapabilityKind.Query,
        Exposure: CapabilityExposure.Rest,
        Discord: null);

    public Task<CapabilityResult> ExecuteAsync(CapabilityInvocation invocation,
                                               CancellationToken cancellationToken) =>
        Task.FromResult(CapabilityResult.Ok(backups.List()));
}
