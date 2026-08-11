using System.Text.RegularExpressions;
using HomelabHub.Abstractions.Configuration;
using HomelabHub.Abstractions.Events;
using HomelabHub.Abstractions.Modules;
using HomelabHub.Core.Capabilities;
using HomelabHub.Core.Events;
using HomelabHub.Core.Ingestion;
using HomelabHub.Core.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HomelabHub.Core;

/// <summary>Enregistrement du noyau et des modules dans le conteneur.</summary>
public static partial class HubCoreServiceCollectionExtensions
{
    /// <summary>
    /// Enregistre le noyau et fait déclarer chaque module.
    /// </summary>
    /// <remarks>
    /// Appelé une fois au démarrage, pour <b>tous</b> les modules, activés ou non : le conteneur
    /// est immuable après <c>Build()</c>, et l'activation est un état runtime (ADR-0002).
    /// </remarks>
    public static IServiceCollection AddHubCore(this IServiceCollection services,
                                                params IHubModule[] modules)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(modules);

        var descriptors = new List<ModuleDescriptor>();
        var errors = new List<string>();

        foreach (var module in modules)
        {
            if (!ModuleKeyPattern().IsMatch(module.Key))
            {
                errors.Add($"Clé de module « {module.Key} » invalide " +
                           "(minuscules, chiffres et tirets, 1 à 20 caractères).");
                continue;
            }

            if (descriptors.Exists(d => string.Equals(d.Key, module.Key, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"Clé de module « {module.Key} » déclarée plusieurs fois.");
                continue;
            }

            var context = new ModuleRegistrationContext(module.Key, services);
            module.Register(context);
            descriptors.Add(new ModuleDescriptor(module, context));
        }

        if (errors.Count > 0)
        {
            throw HubConfigurationException.FromErrors("Déclaration de modules invalide", errors);
        }

        services.AddSingleton(new ModuleCatalog(descriptors));
        services.AddSingleton<IModuleRegistry, ModuleRegistry>();
        services.AddSingleton(typeof(IModuleConfiguration<>), typeof(Configuration.ModuleConfiguration<>));

        // Le registre valide toutes les capacités dans son constructeur et lève si l'une d'elles
        // est mal déclarée. Il est résolu au démarrage (cf. ValidateHubDeclarations) pour que
        // l'échec survienne au lancement et non au premier appel.
        services.AddSingleton<ICapabilityRegistry, CapabilityRegistry>();
        services.AddSingleton<ICapabilityExecutor, CapabilityExecutor>();

        services.AddSingleton<HubJournal>();
        services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<HubJournal>());
        services.AddSingleton<IHubJournal>(sp => sp.GetRequiredService<HubJournal>());

        services.AddSingleton<RefreshCoordinator>();
        services.AddSingleton<IRefreshCoordinator>(sp => sp.GetRequiredService<RefreshCoordinator>());
        services.AddHostedService<ModuleIngestionService>();

        return services;
    }

    /// <summary>
    /// Force la résolution des déclarations pour que toute incohérence casse au démarrage.
    /// </summary>
    /// <remarks>
    /// À appeler juste après <c>Build()</c>. Sans cela, une capacité mal déclarée ne se
    /// manifesterait qu'au premier appel — c'est-à-dire potentiellement des semaines plus tard.
    /// </remarks>
    public static IServiceProvider ValidateHubDeclarations(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _ = services.GetRequiredService<ICapabilityRegistry>();
        return services;
    }

    [GeneratedRegex("^[a-z0-9-]{1,20}$")]
    private static partial Regex ModuleKeyPattern();
}
