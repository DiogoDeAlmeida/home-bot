using System.IO.Compression;
using HomelabHub.Abstractions.Events;
using HomelabHub.Core.Anomalies;
using HomelabHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HomelabHub.Infrastructure.Tests;

/// <summary>
/// La base elle-même : migration, mode WAL, et la copie cohérente qui entre dans l'archive.
/// </summary>
public sealed class HubDatabaseTests
{
    [Fact]
    public void La_migration_cree_la_base_dans_le_repertoire_de_donnees()
    {
        // Le répertoire de données est celui qui survit à une mise à jour et que la sauvegarde
        // couvre (ADR-0007). Une base ailleurs serait perdue au premier déploiement.
        using var hub = new TemporaryHub(withDatabase: true);

        Assert.True(hub.Database!.Exists);
        Assert.StartsWith(hub.Platform.DataDirectory, hub.Database.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Une_base_a_jour_na_plus_aucune_migration_en_attente()
    {
        // C'est ce que lit la séquence de démarrage pour décider s'il faut sauvegarder avant de
        // migrer. Si cette liste n'était jamais vide, le hub archiverait à chaque lancement et
        // la rétention chasserait les archives qui comptent.
        using var hub = new TemporaryHub(withDatabase: true);

        Assert.Empty(hub.Database!.PendingMigrations());
    }

    [Fact]
    public void La_base_est_en_mode_WAL()
    {
        // Sans WAL, une lecture de l'interface bloque l'écriture d'un cycle d'ingestion et
        // inversement — sur un fichier unique, cela se voit tout de suite.
        using var hub = new TemporaryHub(withDatabase: true);
        using var context = hub.Contexts!.CreateDbContext();

        var mode = context.Database
            .SqlQueryRaw<string>("SELECT * FROM pragma_journal_mode() AS Value")
            .AsEnumerable()
            .First();

        Assert.Equal("wal", mode, ignoreCase: true);
    }

    [Fact]
    public void Linstantane_contient_les_ecritures_restees_dans_le_WAL()
    {
        // Le cœur du problème. En mode WAL, une écriture récente n'est pas dans le fichier
        // principal : copier « homelabhub.db » pendant que le hub tourne produit une archive qui
        // s'ouvre, se restaure, et a perdu les dernières minutes. VACUUM INTO, lui, écrit une
        // base complète sans coordination avec les écrivains.
        using var hub = new TemporaryHub(withDatabase: true);
        hub.Anomalies!.Save([Anomaly()]);

        var destination = Path.Combine(hub.Platform.DataDirectory, "instantane.db");
        hub.Database!.SnapshotTo(destination);

        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseSqlite(HubDatabase.ConnectionStringFor(destination))
            .Options;

        using (var copy = new HubDbContext(options))
        {
            Assert.Equal("media.import.pending:aa", Assert.Single(copy.Anomalies).DedupeKey);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void Un_instantane_ecrase_le_precedent_sans_broncher()
    {
        // VACUUM INTO refuse un fichier existant : sans nettoyage préalable, la deuxième
        // sauvegarde échouerait — c'est-à-dire toutes sauf la première.
        using var hub = new TemporaryHub(withDatabase: true);
        var destination = Path.Combine(hub.Platform.DataDirectory, "instantane.db");

        hub.Database!.SnapshotTo(destination);
        hub.Database.SnapshotTo(destination);

        Assert.True(File.Exists(destination));
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    // ── L'archive, avec la base pour de bon ──────────────────────────────────────────

    [Fact]
    public async Task Larchive_contient_la_base_et_pas_ses_fichiers_annexes()
    {
        // Ce qui entre dans l'archive est l'instantané, sous le nom de la base. Les fichiers
        // -wal et -shm n'ont aucun sens hors de leur base d'origine : les embarquer donnerait
        // une restauration incohérente.
        using var hub = new TemporaryHub(withDatabase: true);
        hub.Anomalies!.Save([Anomaly()]);

        var archive = await hub.Backups.CreateAsync("test", TestContext.Current.CancellationToken);

        using var zip = ZipFile.OpenRead(Path.Combine(hub.Platform.BackupsDirectory, archive.FileName));
        var entries = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Contains($"data/{HubDatabase.FileName}", entries);
        Assert.DoesNotContain(entries, e => e.EndsWith("-wal", StringComparison.Ordinal));
        Assert.DoesNotContain(entries, e => e.EndsWith("-shm", StringComparison.Ordinal));
    }

    [Fact]
    public async Task La_base_archivee_est_restaurable_et_contient_les_donnees()
    {
        // Le test qui compte vraiment : une archive dont la base ne s'ouvre pas est une archive
        // qui ne sert à rien, et on ne s'en aperçoit qu'au moment de restaurer.
        using var hub = new TemporaryHub(withDatabase: true);
        hub.Anomalies!.Save([Anomaly()]);

        var archive = await hub.Backups.CreateAsync("test", TestContext.Current.CancellationToken);

        var restored = Path.Combine(hub.Platform.DataDirectory, "restauree.db");

        using (var zip = ZipFile.OpenRead(Path.Combine(hub.Platform.BackupsDirectory, archive.FileName)))
        {
            zip.GetEntry($"data/{HubDatabase.FileName}")!.ExtractToFile(restored);
        }

        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseSqlite(HubDatabase.ConnectionStringFor(restored))
            .Options;

        using (var context = new HubDbContext(options))
        {
            var anomaly = Assert.Single(context.Anomalies);
            Assert.Equal(42, anomaly.Occurrences);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task Le_fichier_temporaire_de_linstantane_ne_reste_pas_sur_le_disque()
    {
        // Sinon il serait ramassé par l'archive suivante, doublant sa taille pour rien.
        using var hub = new TemporaryHub(withDatabase: true);

        await hub.Backups.CreateAsync("test", TestContext.Current.CancellationToken);

        Assert.Empty(Directory.EnumerateFiles(hub.Platform.BackupsDirectory, "*.db"));
    }

    private static Anomaly Anomaly() => new(
        DedupeKey: "media.import.pending:aa",
        ModuleKey: "media",
        Type: "media.import.pending",
        Severity: HubEventSeverity.Warning,
        Title: "Import en attente",
        Body: null,
        Data: null,
        State: AnomalyState.Open,
        OpenedAt: DateTimeOffset.UtcNow,
        LastSeenAt: DateTimeOffset.UtcNow,
        ResolvedAt: null,
        SnoozedUntil: null,
        Occurrences: 42);
}
