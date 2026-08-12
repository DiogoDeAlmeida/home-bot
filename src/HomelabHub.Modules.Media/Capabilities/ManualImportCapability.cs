using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Abstractions.Modules;
using HomelabHub.Modules.Media.Clients;
using HomelabHub.Modules.Media.Correlation;

namespace HomelabHub.Modules.Media.Capabilities;

/// <summary>
/// Déclenche l'import manuel d'un téléchargement bloqué.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mutation avec confirmation obligatoire.</b> Un import déclenché sur le mauvais fichier
/// place un média au mauvais endroit de la bibliothèque : réversible, mais assez ennuyeux pour
/// mériter une question. <c>RequireConfirmation</c> est une propriété de l'opération, pas du
/// canal (ADR-0016) : l'interface web ouvre une modale, un salon ouvrira un bouton de
/// confirmation, et l'API refuse tout autant sans intention explicite.
/// </para>
/// <para>
/// Elle s'appuie sur <c>/api/v3/manualimport</c>, la voie <b>structurée</b> — ses
/// <c>rejections</c> disent ce qui empêche l'import — et non sur la prose de
/// <c>statusMessages</c>, qu'on restitue sans jamais l'interpréter (ADR-0015).
/// </para>
/// </remarks>
internal sealed class ManualImportCapability(
    IRadarrClient radarr,
    ISonarrClient sonarr,
    IModuleState<MediaSnapshot> state) : IHubCapability
{
    public CapabilityDescriptor Descriptor { get; } = new(
        Key: "media.import.manual",
        DisplayName: "Importer manuellement",
        Description: "Force l'import d'un téléchargement terminé que le service refuse d'importer seul.",
        Parameters:
        [
            new CapabilityParameter("download", "Identifiant du téléchargement (voir /media queue)",
                                    CapabilityParameterType.String, Required: true),
        ],
        Kind: CapabilityKind.Mutation,
        Exposure: CapabilityExposure.All,
        Command: new CommandBinding("import"),
        RequireConfirmation: true);

    public async Task<CapabilityResult> ExecuteAsync(CapabilityInvocation invocation,
                                                     CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var joinKey = invocation.GetString("download").ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(joinKey))
        {
            return CapabilityResult.Fail("Aucun téléchargement indiqué.");
        }

        // Le snapshot dit à quel service s'adresser : interroger les deux serait inutile, et
        // importer un film via Sonarr n'aurait aucun sens.
        var match = state.Current.Journeys
            .SelectMany(j => j.Downloads.Select(d => (Journey: j, Download: d)))
            .FirstOrDefault(x => x.Download.JoinKey == joinKey);

        if (match.Journey is null)
        {
            return CapabilityResult.Fail(
                "Ce téléchargement n'est pas dans la vue courante. "
                + "Attendre le prochain cycle, ou vérifier qu'il est encore en file.");
        }

        IArrClient client = match.Journey.MediaType == MediaKind.Movie ? radarr : sonarr;

        // DownloadId et non JoinKey : les routes filtrées par downloadId des *arr sont sensibles
        // à la casse, et une requête en minuscules revient vide sans lever la moindre erreur.
        var candidates = await client
            .GetManualImportCandidatesAsync(match.Download.DownloadId, cancellationToken)
            .ConfigureAwait(false);

        if (!candidates.Success)
        {
            return CapabilityResult.Fail(candidates.Error!);
        }

        var files = candidates.OrEmpty();
        if (files.Count == 0)
        {
            return CapabilityResult.Fail(
                "Aucun fichier à importer — le téléchargement a peut-être déjà été traité.");
        }

        var result = await client.ExecuteManualImportAsync(files, cancellationToken).ConfigureAwait(false);

        // « Accepted » et non « Ok » : la commande est mise en file côté service, sa réussite se
        // constatera au cycle suivant quand l'entrée aura quitté la file.
        return result.Success
            ? CapabilityResult.Accepted(result.Value!)
            : CapabilityResult.Fail(result.Error!);
    }
}
