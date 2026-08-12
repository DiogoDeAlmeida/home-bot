using HomelabHub.Core;
using HomelabHub.Core.Backup;
using HomelabHub.Core.Capabilities;
using HomelabHub.Core.Configuration;
using HomelabHub.Modules.Media;
using HomelabHub.Modules.SystemInfo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace HomelabHub.Host.Tests;

/// <summary>
/// Assemble le même noyau que <c>Program.cs</c> — <c>AddHubCore</c> avec les vrais modules — et
/// résout <see cref="ICapabilityRegistry"/>, dont le constructeur valide chaque capacité déclarée
/// et lève <c>HubConfigurationException</c> à la première erreur.
/// </summary>
/// <remarks>
/// <para>
/// Née d'un incident réel : <c>hub.service.restart</c> déclarait une description de 135
/// caractères, refusée par le validateur au tout premier démarrage de <c>v0.1.2</c> — sur le LXC
/// jetable, pas avant. Le validateur a fait exactement ce qu'on lui demandait, bruyamment ; ce
/// test fait la même vérification, plus tôt, exactement comme <c>ModuleIsolationTests</c>
/// attrape une référence de module interdite avant l'exécution plutôt qu'au premier
/// <c>dotnet build</c> sur le LXC.
/// </para>
/// <para>
/// Les dépendances réelles de l'infrastructure (base SQLite, sauvegarde sur disque) sont
/// remplacées par des doublures triviales : ce test ne vérifie pas que les capacités
/// <i>fonctionnent</i>, seulement qu'elles sont <i>déclarées</i> dans les limites que les canaux
/// conversationnels imposent — la même chose que <c>ValidateHubDeclarations()</c> fait au
/// démarrage réel, avant que Discord ou l'API n'existent.
/// </para>
/// </remarks>
public sealed class CapabilityDeclarationsTests
{
    [Fact]
    public void Toutes_les_capacites_reelles_passent_la_validation_du_noyau()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHubConfigStore>(new RecordingConfigStore());
        services.AddSingleton<IHostApplicationLifetime>(new RecordingLifetime());
        services.AddSingleton<IHubBackupService>(new RecordingBackupService());

        // Les mêmes modules, dans le même ordre, que Program.cs.
        services.AddHubCore(new SystemModule(), new MediaModule());

        using var provider = services.BuildServiceProvider();

        // Lève HubConfigurationException si une capacité est mal déclarée — c'est le seul point
        // de ce test. Une régression ici doit casser la CI, pas un tag de release.
        var registry = provider.GetRequiredService<ICapabilityRegistry>();

        Assert.NotEmpty(registry.All);
    }

    private sealed class RecordingConfigStore : IHubConfigStore
    {
        public string? GetValue(string key) => null;

        public Task SetAsync(string key, string? value, bool secret, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SetManyAsync(IReadOnlyDictionary<string, ConfigValue> values,
                                 CancellationToken cancellationToken) => Task.CompletedTask;

        public IReadOnlyDictionary<string, string> GetByPrefix(string prefix) =>
            new Dictionary<string, string>();

        public bool IsSecret(string key) => false;
    }

    private sealed class RecordingLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }

    private sealed class RecordingBackupService : IHubBackupService
    {
        public Task<BackupArchive> CreateAsync(string reason, CancellationToken cancellationToken) =>
            Task.FromResult(new BackupArchive("test.zip", 0, DateTimeOffset.UtcNow, 0));

        public IReadOnlyList<BackupArchive> List() => [];
    }
}
