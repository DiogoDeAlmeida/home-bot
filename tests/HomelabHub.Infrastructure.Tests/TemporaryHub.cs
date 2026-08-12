using HomelabHub.Core.Configuration;
using HomelabHub.Infrastructure.Backup;
using HomelabHub.Infrastructure.Configuration;
using HomelabHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomelabHub.Infrastructure.Tests;

/// <summary>Une installation jetable du hub, sur disque, pour les tests d'infrastructure.</summary>
/// <remarks>
/// La base est un <b>vrai</b> fichier SQLite migré, pas un fournisseur en mémoire. Le mode WAL,
/// <c>VACUUM INTO</c> et le comportement des index n'existent pas dans le provider InMemory :
/// tester contre lui vérifierait un moteur que le hub n'utilise nulle part.
/// </remarks>
internal sealed class TemporaryHub : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider? _services;

    public TemporaryHub(bool withDatabase = false)
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

        if (withDatabase)
        {
            var path = Path.Combine(Platform.DataDirectory, HubDatabase.FileName);

            _services = new ServiceCollection()
                .AddLogging()
                .AddDbContextFactory<HubDbContext>(b => b.UseSqlite(HubDatabase.ConnectionStringFor(path)))
                .BuildServiceProvider();

            Contexts = _services.GetRequiredService<IDbContextFactory<HubDbContext>>();
            Database = new HubDatabase(Contexts, Platform, NullLogger<HubDatabase>.Instance);
            Database.Migrate();

            Anomalies = new SqliteAnomalyStore(Contexts);
            Journal = new SqliteJournalStore(Contexts);
        }

        Backups = new BackupService(Platform, Options, Store, NullLogger<BackupService>.Instance,
                                    Database);
    }

    public IDbContextFactory<HubDbContext>? Contexts { get; }

    public HubDatabase? Database { get; }

    public SqliteAnomalyStore? Anomalies { get; }

    public SqliteJournalStore? Journal { get; }

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

        // Le pool de connexions SQLite garde le fichier ouvert : sans cette libération, le
        // répertoire refuserait d'être supprimé sous Windows.
        _services?.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

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
