using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Abstractions.Modules;
using HomelabHub.Modules.Media.Clients;
using HomelabHub.Modules.Media.Correlation;

namespace HomelabHub.Modules.Media.Capabilities;

/// <summary>
/// Base commune à l'interruption et à la reprise d'un torrent.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mutation avec confirmation obligatoire</b>, comme l'import manuel : agir directement sur
/// un téléchargement en cours mérite une question, pas un clic isolé (ADR-0016). C'est aussi la
/// règle que <c>ManualImportTests.Toutes_les_mutations_du_module_exigent_une_confirmation</c>
/// vérifie pour toute mutation du module, celle-ci comprise.
/// </para>
/// <para>
/// La recherche se fait par <see cref="DownloadItem.JoinKey"/>, exactement comme l'import
/// manuel — mais l'appel sortant utilise le hash <b>du torrent</b>, pas celui du
/// <c>downloadId</c> : qBittorrent est la source de ce hash, il n'y a ici aucune raison de
/// transiter par la forme que Radarr ou Sonarr auraient donnée.
/// </para>
/// <para>
/// Sans torrent qBittorrent correspondant, il n'y a rien à contrôler : le cas est distinct de
/// « téléchargement introuvable » et le dit explicitement, plutôt que d'échouer sur un hash vide.
/// </para>
/// <para>
/// <b>Portée délibérément limitée à ce que le module suit déjà.</b> La recherche part de
/// <see cref="MediaSnapshot"/>, lui-même construit uniquement à partir des files Radarr et
/// Sonarr (ADR-0015) : un torrent ajouté directement à qBittorrent, sans passer par un média
/// demandé, n'a pas de <see cref="DownloadItem"/> et reste hors d'atteinte de cette capacité.
/// Ce n'est pas une lacune — élargir la portée ferait de ce module une télécommande générique de
/// qBittorrent, ce qu'il n'a jamais eu vocation à être. Vérifié en pratique : le hash de test
/// <c>481b6e36…</c>, ajouté manuellement pour éprouver l'écriture, n'apparaît dans aucun
/// parcours et a donc été validé directement contre le client, hors de cette capacité.
/// </para>
/// </remarks>
internal abstract class TorrentControlCapability(IModuleState<MediaSnapshot> state) : IHubCapability
{
    public abstract CapabilityDescriptor Descriptor { get; }

    protected abstract string Verb { get; }

    protected abstract Task<ServiceResult<bool>> ControlAsync(string hash, CancellationToken cancellationToken);

    public async Task<CapabilityResult> ExecuteAsync(CapabilityInvocation invocation,
                                                      CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var joinKey = invocation.GetString("download").ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(joinKey))
        {
            return CapabilityResult.Fail("Aucun téléchargement indiqué.");
        }

        var download = state.Current.Journeys
            .SelectMany(j => j.Downloads)
            .FirstOrDefault(d => d.JoinKey == joinKey);

        if (download is null)
        {
            return CapabilityResult.Fail(
                "Ce téléchargement n'est pas dans la vue courante. "
                + "Attendre le prochain cycle, ou vérifier qu'il est encore en file.");
        }

        if (download.Torrent is null)
        {
            return CapabilityResult.Fail(
                "Aucun torrent qBittorrent ne correspond à ce téléchargement — "
                + "il a peut-être déjà été retiré du client.");
        }

        var result = await ControlAsync(download.Torrent.Hash, cancellationToken).ConfigureAwait(false);

        // qBittorrent met la commande en file sans confirmer son aboutissement : la réponse dit
        // seulement qu'elle a été reçue, le cycle suivant dira si l'état a changé.
        return result.Success
            ? CapabilityResult.Accepted(
                $"{Verb} demandé pour {download.Title ?? download.JoinKey}. "
                + "Le prochain cycle confirmera le nouvel état.")
            : CapabilityResult.Fail(result.Error!);
    }
}

/// <summary><c>media download pause</c> — interrompt un torrent en cours.</summary>
internal sealed class PauseDownloadCapability(IQBittorrentClient qbittorrent, IModuleState<MediaSnapshot> state)
    : TorrentControlCapability(state)
{
    public override CapabilityDescriptor Descriptor { get; } = new(
        Key: "media.download.pause",
        DisplayName: "Interrompre",
        Description: "Interrompt un téléchargement en cours dans qBittorrent.",
        Parameters:
        [
            new CapabilityParameter("download", "Identifiant du téléchargement",
                                    CapabilityParameterType.String, Required: true),
        ],
        Kind: CapabilityKind.Mutation,
        Exposure: CapabilityExposure.All,
        Command: new CommandBinding("pause"),
        RequireConfirmation: true);

    protected override string Verb => "Arrêt";

    protected override Task<ServiceResult<bool>> ControlAsync(string hash, CancellationToken cancellationToken) =>
        qbittorrent.StopAsync(hash, cancellationToken);
}

/// <summary><c>media download resume</c> — relance un torrent interrompu.</summary>
internal sealed class ResumeDownloadCapability(IQBittorrentClient qbittorrent, IModuleState<MediaSnapshot> state)
    : TorrentControlCapability(state)
{
    public override CapabilityDescriptor Descriptor { get; } = new(
        Key: "media.download.resume",
        DisplayName: "Relancer",
        Description: "Relance un téléchargement interrompu dans qBittorrent.",
        Parameters:
        [
            new CapabilityParameter("download", "Identifiant du téléchargement",
                                    CapabilityParameterType.String, Required: true),
        ],
        Kind: CapabilityKind.Mutation,
        Exposure: CapabilityExposure.All,
        Command: new CommandBinding("resume"),
        RequireConfirmation: true);

    protected override string Verb => "Relance";

    protected override Task<ServiceResult<bool>> ControlAsync(string hash, CancellationToken cancellationToken) =>
        qbittorrent.StartAsync(hash, cancellationToken);
}
