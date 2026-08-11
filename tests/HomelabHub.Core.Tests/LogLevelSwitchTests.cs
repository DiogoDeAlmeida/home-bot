using HomelabHub.Abstractions.Modules;
using HomelabHub.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HomelabHub.Core.Tests;

public sealed class LogLevelSwitchTests
{
    [Fact]
    public void Le_niveau_courant_filtre_les_appels_plus_bas()
    {
        var @switch = new LogLevelSwitch { Minimum = LogLevel.Information };

        Assert.False(@switch.IsEnabled("HomelabHub.Core", LogLevel.Debug));
        Assert.True(@switch.IsEnabled("HomelabHub.Core", LogLevel.Information));
        Assert.True(@switch.IsEnabled("HomelabHub.Core", LogLevel.Error));
    }

    [Fact]
    public void Le_bruit_du_framework_est_planche_a_Warning_en_fonctionnement_normal()
    {
        var @switch = new LogLevelSwitch { Minimum = LogLevel.Information };

        Assert.False(@switch.IsEnabled("Microsoft.AspNetCore.Hosting", LogLevel.Information));
        Assert.True(@switch.IsEnabled("Microsoft.AspNetCore.Hosting", LogLevel.Warning));
    }

    [Fact]
    public void En_diagnostic_le_framework_redevient_bavard()
    {
        // Passer en Debug, c'est vouloir tout voir — y compris ce que fait ASP.NET.
        var @switch = new LogLevelSwitch { Minimum = LogLevel.Debug };

        Assert.True(@switch.IsEnabled("Microsoft.AspNetCore.Hosting", LogLevel.Information));
        Assert.True(@switch.IsEnabled("HomelabHub.Core", LogLevel.Debug));
    }

    [Fact]
    public void AddHubCore_nenregistre_pas_de_second_interrupteur()
    {
        // Régression vécue : le noyau en enregistrait un, le Host un autre. Le conteneur
        // résolvait le second, le filtre écoutait le premier. Le réglage était accepté par
        // l'interface et strictement sans effet — le pire des deux mondes.
        var services = new ServiceCollection();
        var owned = new LogLevelSwitch();
        services.AddSingleton(owned);

        services.AddHubCore(new BareModule());

        using var provider = services.BuildServiceProvider();

        Assert.Same(owned, provider.GetRequiredService<LogLevelSwitch>());
        Assert.Single(services, d => d.ServiceType == typeof(LogLevelSwitch));
    }

    private sealed class BareModule : IHubModule
    {
        public string Key => "bare";

        public string DisplayName => "Doublure";

        public string Description => "Module de test.";

        public Abstractions.Configuration.ModuleConfigSchema ConfigSchema { get; } = new();

        public void Register(IModuleRegistrationContext context)
        {
        }
    }
}
