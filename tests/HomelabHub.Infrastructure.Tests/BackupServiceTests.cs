using System.IO.Compression;
using HomelabHub.Core.Configuration;
using Xunit;

namespace HomelabHub.Infrastructure.Tests;

/// <summary>
/// L'archive unique d'ADR-0007. Ces tests existent pour une raison précise : une base restaurée
/// sans son keyring rend tous les secrets définitivement illisibles, et c'est une erreur qu'on
/// ne découvre qu'au pire moment.
/// </summary>
public sealed class BackupServiceTests
{
    [Fact]
    public async Task Larchive_contient_la_base_le_keyring_et_la_configuration()
    {
        using var hub = new TemporaryHub();
        hub.WriteKeyring();
        hub.WriteDatabase();
        await hub.Store.SetAsync("media.radarr.apiKey", "secret", secret: true,
                                 TestContext.Current.CancellationToken);

        var archive = await hub.Backups.CreateAsync("test", TestContext.Current.CancellationToken);

        var entries = ReadEntries(hub, archive.FileName);

        Assert.Contains(entries, e => e.EndsWith("homelabhub.db", StringComparison.Ordinal));
        Assert.Contains(entries, e => e.Contains("keys/", StringComparison.Ordinal));
        Assert.Contains(entries, e => e.EndsWith("hub.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Larchive_nembarque_pas_les_sauvegardes_precedentes()
    {
        // Sans cette exclusion, chaque sauvegarde contiendrait toutes les précédentes et la
        // taille exploserait de façon géométrique.
        using var hub = new TemporaryHub();
        hub.WriteDatabase();

        await hub.Backups.CreateAsync("première", TestContext.Current.CancellationToken);
        var second = await hub.Backups.CreateAsync("seconde", TestContext.Current.CancellationToken);

        var entries = ReadEntries(hub, second.FileName);

        Assert.DoesNotContain(entries, e => e.Contains("backups/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task La_retention_ne_conserve_que_les_archives_les_plus_recentes()
    {
        using var hub = new TemporaryHub();   // rétention configurée à 3
        hub.WriteDatabase();

        for (var i = 0; i < 5; i++)
        {
            await hub.Backups.CreateAsync($"cycle {i}", TestContext.Current.CancellationToken);
            await Task.Delay(1100, TestContext.Current.CancellationToken); // horodatage à la seconde
        }

        Assert.Equal(3, hub.Backups.List().Count);
    }

    [Fact]
    public async Task La_retention_se_configure_sans_redemarrage()
    {
        using var hub = new TemporaryHub();
        hub.WriteDatabase();

        await hub.Store.SetAsync("hub.backup.retention", "1", secret: false,
                                 TestContext.Current.CancellationToken);

        await hub.Backups.CreateAsync("a", TestContext.Current.CancellationToken);
        await Task.Delay(1100, TestContext.Current.CancellationToken);
        await hub.Backups.CreateAsync("b", TestContext.Current.CancellationToken);

        Assert.Single(hub.Backups.List());
    }

    [Fact]
    public async Task Une_archive_declare_le_nombre_de_fichiers_quelle_contient()
    {
        // Une archive à zéro entrée est le symptôme d'une sauvegarde qui n'a rien sauvegardé.
        using var hub = new TemporaryHub();
        hub.WriteKeyring();
        hub.WriteDatabase();

        var archive = await hub.Backups.CreateAsync("test", TestContext.Current.CancellationToken);

        Assert.True(archive.EntryCount >= 2);
        Assert.True(archive.SizeBytes > 0);
    }

    private static List<string> ReadEntries(TemporaryHub hub, string fileName)
    {
        using var zip = ZipFile.OpenRead(Path.Combine(hub.Platform.BackupsDirectory, fileName));
        return [.. zip.Entries.Select(e => e.FullName)];
    }
}
