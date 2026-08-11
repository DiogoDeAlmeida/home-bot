using HomelabHub.Core.Configuration;
using Xunit;

namespace HomelabHub.Infrastructure.Tests;

public sealed class JsonHubConfigStoreTests
{
    [Fact]
    public async Task Un_secret_nest_jamais_ecrit_en_clair_sur_le_disque()
    {
        using var hub = new TemporaryHub();

        await hub.Store.SetAsync("media.radarr.apiKey", "cle-tres-secrete", secret: true,
                                 TestContext.Current.CancellationToken);

        var raw = await File.ReadAllTextAsync(hub.Platform.ConfigFilePath,
                                              TestContext.Current.CancellationToken);

        Assert.DoesNotContain("cle-tres-secrete", raw, StringComparison.Ordinal);
        Assert.Equal("cle-tres-secrete", hub.Store.GetValue("media.radarr.apiKey"));
        Assert.True(hub.Store.IsSecret("media.radarr.apiKey"));
    }

    [Fact]
    public async Task Une_valeur_non_secrete_reste_lisible_dans_le_fichier()
    {
        using var hub = new TemporaryHub();

        await hub.Store.SetAsync("media.radarr.url", "http://192.168.1.111:7878", secret: false,
                                 TestContext.Current.CancellationToken);

        var raw = await File.ReadAllTextAsync(hub.Platform.ConfigFilePath,
                                              TestContext.Current.CancellationToken);

        Assert.Contains("192.168.1.111", raw, StringComparison.Ordinal);
        Assert.False(hub.Store.IsSecret("media.radarr.url"));
    }

    [Fact]
    public async Task Ecrire_null_supprime_la_cle()
    {
        using var hub = new TemporaryHub();

        await hub.Store.SetAsync("system.disk.warnBelowPercent", "5", secret: false,
                                 TestContext.Current.CancellationToken);
        await hub.Store.SetAsync("system.disk.warnBelowPercent", null, secret: false,
                                 TestContext.Current.CancellationToken);

        Assert.Null(hub.Store.GetValue("system.disk.warnBelowPercent"));
    }

    [Fact]
    public async Task Les_lectures_typees_retombent_sur_la_valeur_par_defaut()
    {
        using var hub = new TemporaryHub();

        await hub.Store.SetAsync("system.pollIntervalSeconds", "90", secret: false,
                                 TestContext.Current.CancellationToken);

        // Une durée saisie en secondes par le formulaire, ou au format TimeSpan à la main :
        // les deux doivent être acceptées.
        Assert.Equal(TimeSpan.FromSeconds(90),
                     hub.Store.GetDuration("system.pollIntervalSeconds", TimeSpan.FromMinutes(5)));
        Assert.Equal(TimeSpan.FromMinutes(5),
                     hub.Store.GetDuration("system.absente", TimeSpan.FromMinutes(5)));
        Assert.Equal(42, hub.Store.GetInt32("system.absente", 42));
        Assert.True(hub.Store.GetBoolean("system.absente", fallback: true));
    }

    [Fact]
    public async Task Le_prefixe_permet_de_lister_la_configuration_dun_module()
    {
        using var hub = new TemporaryHub();

        await hub.Store.SetManyAsync(new Dictionary<string, ConfigValue>
        {
            ["media.radarr.url"] = new("http://radarr", Secret: false),
            ["media.sonarr.url"] = new("http://sonarr", Secret: false),
            ["system.enabled"] = new("true", Secret: false),
        }, TestContext.Current.CancellationToken);

        var media = hub.Store.GetByPrefix("media.");

        Assert.Equal(2, media.Count);
        Assert.DoesNotContain("system.enabled", media.Keys);
    }
}
