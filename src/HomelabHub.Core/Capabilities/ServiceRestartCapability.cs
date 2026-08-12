using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Core.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HomelabHub.Core.Capabilities;

/// <summary>
/// Redémarre le processus du hub.
/// </summary>
/// <remarks>
/// <para>
/// Capacité du noyau, comme <c>hub.anomaly.snooze</c> et <c>hub.journal.purge</c> : aucun module
/// ne possède le processus lui-même. Née d'un besoin concret trouvé en conditions réelles sur le
/// LXC jetable — la configuration Discord (jeton, serveur, salon, rôle) n'est lue qu'au démarrage
/// de <c>DiscordGatewayService</c>, jamais rechargée à chaud, contrairement à d'autres réglages
/// (niveau de journalisation, intervalles des pollers) qui le sont déjà. Avant cette capacité, la
/// seule façon d'appliquer un tel changement était une console SSH.
/// </para>
/// <para>
/// <b>Avec confirmation</b> : contrairement à la mise en sommeil d'une anomalie, ceci interrompt
/// le service en cours, pour tout le monde, quelques secondes.
/// </para>
/// <para>
/// <b>La réponse part avant l'arrêt.</b> Le processus qui exécute cette capacité est celui qui va
/// s'arrêter — <see cref="IHostApplicationLifetime.StopApplication"/> n'est donc pas appelée ici,
/// mais après un court délai, hors du chemin d'exécution qui produit <see cref="CapabilityResult"/>.
/// Sans ce délai, l'arrêt du service Discord (<c>DiscordGatewayService.ExecuteAsync</c>, dans son
/// <c>finally</c>) pourrait couper la connexion avant que la confirmation Discord n'atteigne ses
/// serveurs, et la réponse REST avant que Kestrel n'ait fini de l'écrire.
/// </para>
/// <para>
/// <b>Et l'unité systemd doit relancer un arrêt volontaire</b>, pas seulement un plantage :
/// <c>Restart=on-failure</c> ne redémarre jamais un processus qui se termine proprement (code 0),
/// qu'il s'agisse d'un <c>systemctl stop</c> ou d'un appel à <c>StopApplication()</c> — les deux
/// sont indiscernables de son point de vue. <c>deploy/systemd/homelabhub.service</c> porte donc
/// <c>Restart=always</c> à la place (ADR-0019) : un <c>systemctl stop</c> explicite reste
/// respecté — systemd n'applique jamais la politique de redémarrage à un arrêt qu'il a
/// lui-même demandé — seul un arrêt que le processus déclenche de lui-même en est un.
/// </para>
/// </remarks>
internal sealed class ServiceRestartCapability(
    IHostApplicationLifetime lifetime,
    ILogger<ServiceRestartCapability> logger) : IHubCapability
{
    /// <summary>
    /// Assez pour qu'une réponse Discord ou REST parte réellement, pas assez pour que
    /// l'utilisateur se demande si le clic a été pris en compte.
    /// </summary>
    private static readonly TimeSpan ResponseGrace = TimeSpan.FromSeconds(2);

    public CapabilityDescriptor Descriptor { get; } = new(
        Key: $"{HubSettings.Prefix}.service.restart",
        DisplayName: "Redémarrer le service",
        Description: "Redémarre le hub — nécessaire après un changement de configuration Discord " +
                      "(jeton, serveur, salon, rôle), qui n'est lu qu'au démarrage.",
        Parameters: [],
        Kind: CapabilityKind.Mutation,
        Exposure: CapabilityExposure.All,
        Command: new CommandBinding("service", "restart"),
        RequireConfirmation: true);

    public Task<CapabilityResult> ExecuteAsync(CapabilityInvocation invocation,
                                               CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        logger.LogWarning("Redémarrage du service demandé par {Actor}.", invocation.ActorId);

        // CancellationToken.None délibérément : ce délai ne doit pas s'arrêter si le jeton de la
        // requête qui l'a déclenché expire ou est annulé avant lui — c'est justement ce jeton-là
        // que StopApplication() va invalider une fois le délai écoulé.
        _ = Task.Run(async () =>
        {
            await Task.Delay(ResponseGrace, CancellationToken.None).ConfigureAwait(false);
            lifetime.StopApplication();
        }, CancellationToken.None);

        return Task.FromResult(CapabilityResult.Ok(
            "Redémarrage en cours — le service sera indisponible quelques secondes."));
    }
}
