using HomelabHub.Core.Anomalies;
using Microsoft.Extensions.DependencyInjection;

namespace HomelabHub.Discord;

public static class DiscordAdapterServiceCollectionExtensions
{
    /// <summary>
    /// Enregistre la passerelle Discord. Sans jeton ni serveur en configuration, elle démarre et
    /// s'arrête aussitôt sans rien tenter — Discord est une surface optionnelle, pas une
    /// dépendance du hub.
    /// </summary>
    /// <remarks>
    /// Un seul singleton, exposé sous trois façades : service d'arrière-plan pour la passerelle,
    /// et <see cref="IAnomalyNotifier"/> pour recevoir les transitions que
    /// <c>ModuleIngestionService</c> produit. Le noyau ne sait pas qu'il s'agit de Discord — il
    /// appelle l'interface, exactement comme pour l'autorisation (ADR-0004, ADR-0016).
    /// </remarks>
    public static IServiceCollection AddDiscordAdapter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<DiscordGatewayService>();
        services.AddHostedService(sp => sp.GetRequiredService<DiscordGatewayService>());
        services.AddSingleton<IAnomalyNotifier>(sp => sp.GetRequiredService<DiscordGatewayService>());

        return services;
    }
}
