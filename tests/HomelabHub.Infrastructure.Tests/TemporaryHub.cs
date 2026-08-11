using HomelabHub.Core.Configuration;
using HomelabHub.Infrastructure.Backup;
using HomelabHub.Infrastructure.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomelabHub.Infrastructure.Tests;

/// <summary>Une installation jetable du hub, sur disque, pour les tests d'infrastructure.</summary>
internal sealed class TemporaryHub : IDisposable
{
    private readonly string _root;

    public TemporaryHub()
    {
        _root = Path.Combine(Path.GetTempPath(), "homelabhub-tests", Guid.NewGuid().ToString("N"));

        Options = new HubOptions
        {
            DataDirectory = Path.Combine(_root, "data"),
            ConfigDirectory = Path.Combine(_root, "config"),
            BackupRetention = 3,
        };

        Platform = new HubPlatform(Options);
        Store = new JsonHubConfigStore(Platform, new EphemeralDataProtectionProvider(),
                                       NullLogger<JsonHubConfigStore>.Instance);
        Backups = new BackupService(Platform, Options, Store, NullLogger<BackupService>.Instance);
    }

    public HubOptions Options { get; }

    public HubPlatform Platform { get; }

    public JsonHubConfigStore Store { get; }

    public BackupService Backups { get; }

    /// <summary>Simule le keyring Data Protection déposé par l'infrastructure au démarrage.</summary>
    public void WriteKeyring(string content = "<key id=\"test\" />")
    {
        Directory.CreateDirectory(Platform.KeysDirectory);
        File.WriteAllText(Path.Combine(Platform.KeysDirectory, "key-test.xml"), content);
    }

    /// <summary>Simule la base SQLite, qui n'existe pas encore mais doit être couverte d'avance.</summary>
    public void WriteDatabase(string content = "SQLite format 3")
    {
        Directory.CreateDirectory(Platform.DataDirectory);
        File.WriteAllText(Path.Combine(Platform.DataDirectory, "homelabhub.db"), content);
    }

    public void Dispose()
    {
        Backups.Dispose();
        Store.Dispose();

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Un fichier encore verrouillé sous Windows ne doit pas faire échouer un test vert.
        }
    }
}
