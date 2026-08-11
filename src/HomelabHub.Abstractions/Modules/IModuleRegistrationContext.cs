using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Abstractions.Dashboard;
using HomelabHub.Abstractions.Ingest;
using Microsoft.Extensions.DependencyInjection;

namespace HomelabHub.Abstractions.Modules;

/// <summary>
/// Surface offerte à un module pour se déclarer, sans jamais exposer le noyau.
/// </summary>
/// <remarks>
/// La conception suit une seule idée (ADR-0003) : <b>les modes d'ingestion diffèrent en
/// amont, mais convergent en aval</b>. Poller, webhook et connexion ont des cycles de vie
/// irréconciliables — l'un est piloté par un minuteur du noyau, l'autre par une requête
/// HTTP entrante, le troisième par une boucle que le module tient lui-même. En revanche
/// tous trois écrivent dans le même <see cref="IModuleState{TSnapshot}"/> et publient
/// dans le même flux d'événements. Les consommateurs (widgets, SignalR, dashboard
/// Discord) ne savent jamais qui a parlé.
/// </remarks>
public interface IModuleRegistrationContext
{
    /// <summary>Clé du module en cours d'enregistrement (<see cref="IHubModule.Key"/>).</summary>
    string ModuleKey { get; }

    /// <summary>
    /// Accès direct au conteneur, pour ce que les méthodes ci-dessous ne couvrent pas.
    /// Enregistrer un <c>IHostedService</c> par ici est un contresens : le module
    /// échapperait au contrôle d'activation et à la supervision du noyau. Utiliser
    /// <see cref="AddPoller{T}"/> ou <see cref="AddConnection{T}"/>.
    /// </summary>
    IServiceCollection Services { get; }

    // ─────────────────────────────────────────────────────────────────────────────
    //  ÉTAT — le point de convergence
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Déclare le snapshot du module : une donnée immuable, écrite par les sources
    /// d'ingestion et lue par tout le reste.
    /// </summary>
    /// <param name="initial">
    /// Valeur initiale, représentant l'état « rien de connu encore ». Doit être valide
    /// et affichable : le dashboard peut la lire avant le premier cycle d'ingestion.
    /// </param>
    IModuleRegistrationContext AddState<TSnapshot>(TSnapshot initial)
        where TSnapshot : class;

    // ─────────────────────────────────────────────────────────────────────────────
    //  INGESTION — trois cycles de vie, une seule sortie
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Interrogation périodique pilotée par le noyau. Source de vérité de l'état.
    /// </summary>
    /// <param name="defaultInterval">Intervalle par défaut si rien n'est configuré.</param>
    /// <param name="intervalConfigKey">
    /// Clé de configuration (relative au module) permettant de surcharger l'intervalle
    /// depuis l'interface web. <c>null</c> pour un intervalle non configurable.
    /// </param>
    IModuleRegistrationContext AddPoller<T>(TimeSpan defaultInterval,
                                            string? intervalConfigKey = null)
        where T : class, IModulePoller;

    /// <summary>
    /// Réception de notifications poussées par un service externe. Le noyau expose
    /// <c>POST /api/webhooks/{ModuleKey}/{hookName}</c>, authentifie l'appel, puis route
    /// le corps ici. Le module ne voit ni le jeton, ni la route, ni le contrôle d'accès.
    /// </summary>
    /// <param name="hookName">
    /// Segment de route, minuscules, <c>[a-z0-9-]</c>. Un module peut déclarer plusieurs
    /// hooks pour distinguer les sources (par exemple <c>radarr</c> et <c>sonarr</c>).
    /// </param>
    IModuleRegistrationContext AddWebhook<T>(string hookName)
        where T : class, IModuleWebhookHandler;

    /// <summary>
    /// Connexion longue durée tenue par le module (WebSocket, SSE, socket applicatif).
    /// Le noyau la démarre à l'activation, l'annule à la désactivation, la rétablit
    /// avec backoff exponentiel et remonte son état dans la santé du module.
    /// </summary>
    IModuleRegistrationContext AddConnection<T>()
        where T : class, IModuleConnection;

    // ─────────────────────────────────────────────────────────────────────────────
    //  EXPOSITION
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Déclare une opération invocable depuis l'API et/ou un canal conversationnel.</summary>
    IModuleRegistrationContext AddCapability<T>() where T : class, IHubCapability;

    /// <summary>Déclare un bloc de données pour le tableau de bord.</summary>
    IModuleRegistrationContext AddWidget<T>() where T : class, IWidgetProvider;

    /// <summary>
    /// Déclare une sonde de santé. Séparée de <see cref="IHubModule"/> parce qu'elle a
    /// besoin des clients résolus par le conteneur, lequel n'existe pas encore au moment
    /// où <see cref="IHubModule"/> est instancié.
    /// </summary>
    IModuleRegistrationContext AddHealthCheck<T>() where T : class, IModuleHealthCheck;

    // ─────────────────────────────────────────────────────────────────────────────
    //  PLOMBERIE FOURNIE PAR LE NOYAU
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Enregistre un client HTTP typé, préconfiguré par le noyau : délai d'attente
    /// explicite, réessais, disjoncteur, journalisation expurgée des secrets, adresse de
    /// base et authentification résolues depuis la configuration du module et rechargées
    /// à chaud.
    /// </summary>
    /// <param name="configKeyPrefix">
    /// Préfixe des clés de configuration décrivant ce service (par exemple <c>radarr</c>
    /// pour <c>radarr.url</c> et <c>radarr.apiKey</c>).
    /// </param>
    /// <remarks>
    /// Objectif : qu'un module n'écrive jamais de politique réseau. Un service injoignable
    /// doit dégrader l'affichage, pas propager une exception — cette garantie appartient
    /// au noyau, pas à chaque auteur de module.
    /// </remarks>
    IHttpClientBuilder AddServiceClient<TClient, TImpl>(string configKeyPrefix)
        where TClient : class
        where TImpl : class, TClient;
}
