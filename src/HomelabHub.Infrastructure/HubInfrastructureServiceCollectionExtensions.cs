using HomelabHub.Abstractions.Platform;
using HomelabHub.Core.Anomalies;
using HomelabHub.Core.Backup;
using HomelabHub.Core.Configuration;
using HomelabHub.Core.Events;
using HomelabHub.Infrastructure.Backup;
using HomelabHub.Infrastructure.Configuration;
using HomelabHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomelabHub.Infrastructure;

public static class HubInfrastructureServiceCollectionExtensions
{
    /// <summary>Chemins, protection des données, magasin de configuration et sauvegarde.</summary>
    public static IServiceCollection AddHubInfrastructure(this IServiceCollection services,
                                                          IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new HubOptions();
        configuration.GetSection(HubOptions.SectionName).Bind(options);
        services.AddSingleton(options);

        var platform = new HubPlatform(options);
        services.AddSingleton(platform);
        services.AddSingleton<IHubPlatform>(platform);

        // Avant tout le reste : une deuxième instance qui continuerait au-delà de cette ligne
        // écrirait le keyring, la configuration et la base en concurrence avec la première, sans
        // qu'aucune des deux ne le sache. Trouvé en production — voir SingleInstanceLock.
        // L'exception se propage jusqu'à Program.cs, qui refuse de démarrer avec un message
        // explicite plutôt que de laisser passer un échec silencieux.
        services.AddSingleton(SingleInstanceLock.Acquire(platform.DataDirectory));

        // Le keyring vit dans le répertoire de données, donc dans l'archive de sauvegarde.
        // Sur Linux il n'y a pas de DPAPI : ce sont des fichiers XML en clair, protégés par
        // les permissions du système de fichiers et rien d'autre (ADR-0007).
        services.AddDataProtection()
                .PersistKeysToFileSystem(Directory.CreateDirectory(platform.KeysDirectory))
                .SetApplicationName("HomelabHub");

        services.AddSingleton<IHubConfigStore, JsonHubConfigStore>();

        // La base vit dans le répertoire de données : couverte par la sauvegarde, épargnée par
        // une mise à jour (ADR-0007). Une fabrique plutôt qu'un DbContext injecté : les magasins
        // sont des singletons appelés depuis autant de boucles qu'il y a de pollers, et un
        // DbContext n'est pas sûr entre threads.
        var databasePath = Path.Combine(platform.DataDirectory, HubDatabase.FileName);
        services.AddDbContextFactory<HubDbContext>(builder =>
            builder.UseSqlite(HubDatabase.ConnectionStringFor(databasePath)));

        services.AddSingleton<HubDatabase>();

        // Enregistrés avant AddHubCore, qui n'ajoute ses implémentations en mémoire que si
        // personne n'en a fourni : monter l'infrastructure suffit à rendre l'état durable.
        services.AddSingleton<IAnomalyStore, SqliteAnomalyStore>();
        services.AddSingleton<IJournalStore, SqliteJournalStore>();

        // IHubBackupService reste une dépendance du noyau et du Host : aucun module ne peut le
        // résoudre, puisqu'aucun module ne référence Core (ADR-0014).
        services.AddSingleton<IHubBackupService, BackupService>();

        return services;
    }
}
