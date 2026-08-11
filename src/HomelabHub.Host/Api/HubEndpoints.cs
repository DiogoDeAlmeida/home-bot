using HomelabHub.Core.Backup;
using HomelabHub.Core.Configuration;

namespace HomelabHub.Host.Api;

/// <summary>
/// Surface propre au noyau : ses réglages et ses sauvegardes.
/// </summary>
/// <remarks>
/// Aux yeux de l'interface, le noyau est un pseudo-module — même schéma, même projection, même
/// générateur de formulaire (ADR-0013). Dans le contrat, il n'en est pas un.
/// </remarks>
internal static class HubEndpoints
{
    public static void MapHub(this IEndpointRouteBuilder app)
    {
        var settings = app.MapGroup("/api/settings").RequireAuthorization();

        settings.MapGet("/", (IHubConfigStore store) =>
            Results.Ok(ConfigSurface.Describe(HubSettings.Prefix, HubSettings.Schema.Fields, store)));

        settings.MapPut("/", async (Dictionary<string, string?> values, IHubConfigStore store,
                                    LogLevelSwitch logLevel, CancellationToken cancellationToken) =>
        {
            var result = await ConfigSurface
                .WriteAsync(HubSettings.Prefix, HubSettings.Schema.Fields, values, store, cancellationToken)
                .ConfigureAwait(false);

            // Le niveau de journalisation est lu par un délégué de filtrage évalué à chaque
            // appel : la nouvelle valeur s'applique dès cette ligne, sans redémarrage.
            logLevel.ApplyFrom(store);

            return result;
        });

        // Les archives relèvent du noyau, pas d'un module : un module peut demander une
        // sauvegarde, il n'a pas à énumérer les archives (ADR-0014).
        app.MapGet("/api/backups", (IHubBackupService backups) => Results.Ok(backups.List()))
           .RequireAuthorization();
    }
}
