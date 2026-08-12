using System.Net;
using HomelabHub.Abstractions.Configuration;
using HomelabHub.Abstractions.Modules;
using HomelabHub.Modules.Media.Capabilities;
using HomelabHub.Modules.Media.Clients;
using HomelabHub.Modules.Media.Correlation;
using Microsoft.Extensions.DependencyInjection;

namespace HomelabHub.Modules.Media;

/// <summary>
/// Le domaine média : requêtes, téléchargements et imports, corrélés en une vue unique.
/// </summary>
/// <remarks>
/// L'intérêt du module n'est pas d'afficher quatre services côte à côte, mais de <b>rapprocher</b>
/// la requête Seerr, la file Radarr ou Sonarr et le torrent qBittorrent, qui sont trois vues du
/// même objet. Les clés de jointure ont été vérifiées sur les instances réelles, pas supposées
/// (ADR-0015).
/// </remarks>
public sealed class MediaModule : IHubModule
{
    public const string PollIntervalKey = "pollIntervalSeconds";
    public const string HistoryPageSizeKey = "historyPageSize";
    public const string StalledAfterKey = "detect.stalledAfter";
    public const string GraceAfterAddedKey = "detect.graceAfterAdded";
    public const string UnreachableCyclesKey = "detect.unreachableCycles";

    public string Key => "media";

    public string DisplayName => "Média";

    public string Description =>
        "Requêtes, téléchargements et imports, corrélés de la demande à la disponibilité.";

    public ModuleConfigSchema ConfigSchema => new ModuleConfigSchema()
        .AddUrl("radarr.url", "URL Radarr", required: true, help: "Par exemple http://192.168.1.233:7878")
        .AddSecret("radarr.apiKey", "Clé API Radarr", required: true,
                   help: "Paramètres → Général → Sécurité → Clé API.")
        .AddUrl("sonarr.url", "URL Sonarr", required: true, help: "Par exemple http://192.168.1.232:8989")
        .AddSecret("sonarr.apiKey", "Clé API Sonarr", required: true)
        .AddUrl("seerr.url", "URL Seerr", required: true, help: "Par exemple http://192.168.1.231:5055")
        .AddSecret("seerr.apiKey", "Clé API Seerr", required: true)
        .AddUrl("qbittorrent.url", "URL qBittorrent", required: true,
                help: "Par exemple http://192.168.1.240:8090")
        .AddText("qbittorrent.username", "Utilisateur qBittorrent",
                 help: "qBittorrent ne gère qu'un seul compte : c'est celui de son interface web.")
        .AddSecret("qbittorrent.password", "Mot de passe qBittorrent")
        .AddDuration(PollIntervalKey, "Intervalle d'interrogation", TimeSpan.FromSeconds(60),
                     help: "Les webhooks donnent la réactivité ; ce cycle donne la vérité.")
        .AddInt(HistoryPageSizeKey, "Événements d'historique lus par cycle", defaultValue: 100,
                help: "Sert à déterminer si un téléchargement disparu de la file a été importé ou a échoué. "
                      + "Un pack de saison produit à lui seul 44 événements.")
        .AddDuration(StalledAfterKey, "Blocage signalé après une inactivité de",
                     TimeSpan.FromMinutes(30),
                     help: "Mesuré sur la dernière activité rapportée par qBittorrent, pas entre deux cycles.")
        .AddDuration(GraceAfterAddedKey, "Délai de grâce après ajout", TimeSpan.FromMinutes(10),
                     help: "Un torrent tout juste récupéré se déclare « stalled » avant d'avoir trouvé un "
                           + "pair. Sans ce délai, chaque téléchargement lèverait une alerte.")
        .AddInt(UnreachableCyclesKey, "Cycles avant de signaler un service injoignable", defaultValue: 2,
                help: "Absorbe le redémarrage d'un LXC sans déclencher d'alerte.");

    public void Register(IModuleRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.AddServiceClient<IRadarrClient, RadarrClient>("radarr");
        context.AddServiceClient<ISonarrClient, SonarrClient>("sonarr");
        context.AddServiceClient<ISeerrClient, SeerrClient>("seerr");

        // qBittorrent ne s'authentifie pas par clé d'API mais par cookie de session : il lui
        // faut un conteneur de cookies, et un Referer cohérent avec l'adresse de base pour
        // passer la protection CSRF active par défaut depuis la 4.2.
        context.AddServiceClient<IQBittorrentClient, QBittorrentClient>("qbittorrent")
               .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
               {
                   CookieContainer = new CookieContainer(),
                   UseCookies = true,
               })
               .ConfigureHttpClient((provider, client) =>
               {
                   if (client.BaseAddress is { } baseAddress)
                   {
                       client.DefaultRequestHeaders.Referrer = baseAddress;
                   }
               });

        context.AddState(MediaSnapshot.Empty)
               .AddPoller<MediaPoller>(TimeSpan.FromSeconds(60), PollIntervalKey)
               .AddHealthCheck<MediaHealthCheck>()
               .AddCapability<QueueOverviewCapability>()
               .AddCapability<QueueDetailCapability>()
               .AddCapability<ManualImportCapability>()
               .AddCapability<PauseDownloadCapability>()
               .AddCapability<ResumeDownloadCapability>()
               .AddWidget<MediaWidget>();
    }
}
