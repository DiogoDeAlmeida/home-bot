using HomelabHub.Abstractions.Configuration;
using HomelabHub.Abstractions.Modules;
using HomelabHub.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HomelabHub.Core.Tests;

public sealed class ModuleRegistrationTests
{
    [Fact]
    public void Un_module_ne_peut_pas_revendiquer_le_prefixe_reserve_du_noyau()
    {
        // « hub. » porte la rétention des sauvegardes et le niveau de journalisation
        // (ADR-0013). Un module qui prendrait cette clé les écraserait.
        var exception = Assert.Throws<HubConfigurationException>(() =>
            new ServiceCollection().AddHubCore(new StubModule("hub")));

        Assert.Contains("réservée", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Media")]          // majuscules
    [InlineData("mon_module")]     // souligné
    [InlineData("")]               // vide
    [InlineData("module-beaucoup-trop-long")]
    public void Une_cle_de_module_invalide_fait_echouer_le_demarrage(string key)
    {
        Assert.Throws<HubConfigurationException>(() =>
            new ServiceCollection().AddHubCore(new StubModule(key)));
    }

    [Fact]
    public void Deux_modules_ne_peuvent_pas_partager_la_meme_cle()
    {
        var exception = Assert.Throws<HubConfigurationException>(() =>
            new ServiceCollection().AddHubCore(new StubModule("media"), new StubModule("media")));

        Assert.Contains("plusieurs fois", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Une_cle_valide_est_acceptee()
    {
        var services = new ServiceCollection().AddHubCore(new StubModule("media"));

        Assert.Contains(services, d => d.ServiceType == typeof(Modules.ModuleCatalog));
    }

    [Fact]
    public void Le_schema_du_hub_et_celui_dun_module_partagent_la_meme_primitive()
    {
        // C'est le test que la réutilisabilité est réelle, et pas seulement affirmée : les deux
        // produisent des ConfigField identiques, donc un seul générateur de formulaire suffit.
        var module = new ModuleConfigSchema().AddInt("seuil", "Seuil", defaultValue: 10);
        var hub = new HubConfigSchema().AddInt("seuil", "Seuil", defaultValue: 10);

        Assert.Equal(module.Fields[0], hub.Fields[0]);
    }

    [Fact]
    public void Le_schema_du_noyau_declare_ses_reglages()
    {
        var keys = HubSettings.Schema.Fields.Select(f => f.Key).ToArray();

        Assert.Contains("backup.retention", keys);
        Assert.Contains("backup.minimumInterval", keys);
        Assert.Contains("logging.level", keys);
    }

    [Fact]
    public void Un_champ_declare_deux_fois_est_refuse()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ModuleConfigSchema().AddInt("seuil", "Seuil").AddText("seuil", "Doublon"));
    }

    private sealed class StubModule(string key) : IHubModule
    {
        public string Key { get; } = key;

        public string DisplayName => "Doublure";

        public string Description => "Module de test.";

        public ModuleConfigSchema ConfigSchema { get; } = new();

        public void Register(IModuleRegistrationContext context)
        {
        }
    }
}
