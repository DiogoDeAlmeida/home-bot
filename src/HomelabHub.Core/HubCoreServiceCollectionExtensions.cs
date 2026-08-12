using System.Text.RegularExpressions;
using HomelabHub.Abstractions.Configuration;
using HomelabHub.Abstractions.Events;
using HomelabHub.Abstractions.Modules;
using HomelabHub.Abstractions.Platform;
using HomelabHub.Core.Capabilities;
using HomelabHub.Core.Configuration;
using HomelabHub.Core.Events;
using HomelabHub.Core.Ingestion;
using HomelabHub.Core.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

            // « hub » est réservé aux réglages du noyau (ADR-0013). Un module qui le
            // revendiquerait écraserait la rétention des sauvegardes ou le niveau de log.
            if (string.Equals(module.Key, HubSettings.Prefix, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Clé de module « {module.Key} » réservée aux réglages du noyau.");
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

        // Un module demande une sauvegarde ; il ne la pilote pas (ADR-0014).
        services.AddSingleton(typeof(IBackupRequester<>), typeof(Backup.BackupRequester<>));
        services.AddSingleton<Backup.BackupThrottle>();
        services.TryAddSingleton(TimeProvider.System);

        // LogLevelSwitch n'est délibérément PAS enregistré ici : le Host doit l'instancier
        // avant Build() pour le brancher sur le filtre de journalisation, et l'enregistrer
        // lui-même. En ajouter un second ici produirait une instance résolue par le conteneur
        // que personne n'écoute — le réglage serait accepté et sans effet.

        // Le registre valide toutes les capacités dans son constructeur et lève si l'une d'elles
        // est mal déclarée. Il est résolu au démarrage (cf. ValidateHubDeclarations) pour que
        // l'échec survienne au lancement et non au premier appel.
        services.AddSingleton<ICapabilityRegistry, CapabilityRegistry>();
        services.AddSingleton<ICapabilityExecutor, CapabilityExecutor>();

        // Magasins par défaut : rien sur disque. L'infrastructure les remplace par leurs versions
        // SQLite via TryAdd inversé — elle enregistre les siens avant AddHubCore, et ces deux
        // lignes ne font que garantir qu'un noyau monté seul, en test, reste fonctionnel.
        services.TryAddSingleton<Anomalies.IAnomalyStore, Anomalies.NullAnomalyStore>();
        services.TryAddSingleton<IJournalStore, InMemoryJournalStore>();

        services.AddSingleton<Anomalies.AnomalyEngine>();
        services.AddSingleton<Anomalies.IAnomalyEngine>(sp => sp.GetRequiredService<Anomalies.AnomalyEngine>());

        services.AddSingleton<HubJournal>();
        services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<HubJournal>());
        services.AddSingleton<IHubJournal>(sp => sp.GetRequiredService<HubJournal>());

        services.AddSingleton<RefreshCoordinator>();
        services.AddSingleton<IRefreshCoordinator>(sp => sp.GetRequiredService<RefreshCoordinator>());
        services.AddHostedService<ModuleIngestionService>();
        services.AddHostedService<RetentionService>();

        return services;
    }

    /// <summary>
    /// Recharge l'état possédé par le hub — aujourd'hui la table d'anomalies.
    /// </summary>
    /// <remarks>
    /// À appeler après la migration de la base et <b>avant</b> que le premier cycle d'ingestion
    /// n'ait lieu : une anomalie rechargée après son propre cycle serait vue comme nouvelle, et
    /// renotifiée — soit exactement le bruit que la persistance sert à supprimer.
    /// </remarks>
    public static IServiceProvider HydrateHubState(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var catalog = services.GetRequiredService<ModuleCatalog>();
        var keys = catalog.Descriptors.Select(d => d.Key).ToList();

        services.GetRequiredService<Anomalies.AnomalyEngine>()
                .Hydrate(keys, DateTimeOffset.UtcNow);

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
