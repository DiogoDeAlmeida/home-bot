using HomelabHub.Abstractions.Platform;
using HomelabHub.Core.Backup;
using HomelabHub.Core.Configuration;
using HomelabHub.Infrastructure.Backup;
using HomelabHub.Infrastructure.Configuration;
using Microsoft.AspNetCore.DataProtection;
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

        // Le keyring vit dans le répertoire de données, donc dans l'archive de sauvegarde.
        // Sur Linux il n'y a pas de DPAPI : ce sont des fichiers XML en clair, protégés par
        // les permissions du système de fichiers et rien d'autre (ADR-0007).
        services.AddDataProtection()
                .PersistKeysToFileSystem(Directory.CreateDirectory(platform.KeysDirectory))
                .SetApplicationName("HomelabHub");

        services.AddSingleton<IHubConfigStore, JsonHubConfigStore>();

        // IHubBackupService reste une dépendance du noyau et du Host : aucun module ne peut le
        // résoudre, puisqu'aucun module ne référence Core (ADR-0014).
        services.AddSingleton<IHubBackupService, BackupService>();

        return services;
    }
}
