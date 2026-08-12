using Microsoft.Extensions.DependencyInjection;

namespace HomelabHub.Discord;

public static class DiscordAdapterServiceCollectionExtensions
{
    /// <summary>
    /// Enregistre la passerelle Discord. Sans jeton ni serveur en configuration, elle démarre et
    /// s'arrête aussitôt sans rien tenter — Discord est une surface optionnelle, pas une
    /// dépendance du hub.
    /// </summary>
    public static IServiceCollection AddDiscordAdapter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHostedService<DiscordGatewayService>();

        return services;
    }
}
