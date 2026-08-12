using Discord;
using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Core.Capabilities;
using HomelabHub.Discord.Commands;
using Xunit;

namespace HomelabHub.Discord.Tests;

/// <summary>
/// Traduction des capacités en commandes Discord, sans passerelle réelle.
/// </summary>
/// <remarks>
/// Le point sensible n'est pas la forme des commandes elle-même, mais que
/// <c>RouteToCapabilityKey</c> retrouve exactement la même clé que ce que
/// <see cref="DiscordInteractionRoute"/> reconstruira depuis une interaction réelle — c'est ce
/// lien qui route un clic vers la bonne capacité.
/// </remarks>
public sealed class DiscordCommandBuilderTests
{
    private static RegisteredCapability Capability(
        string moduleKey, string key, CommandBinding? command,
        IReadOnlyList<CapabilityParameter>? parameters = null) =>
        new(moduleKey,
            new CapabilityDescriptor(key, key, "Description de test.", parameters ?? [],
                CapabilityKind.Query, CapabilityExposure.All, command),
            new StubCapability());

    [Fact]
    public void Une_racine_de_commande_par_module()
    {
        var plan = DiscordCommandBuilder.Build([
            Capability("media", "media.queue", new CommandBinding("queue")),
            Capability("system", "system.status", new CommandBinding("status")),
        ]);

        Assert.Equal(["media", "system"], plan.Commands.Select(c => c.Name.Value).Order());
    }

    [Fact]
    public void Un_chemin_dun_segment_devient_une_sous_commande_directe()
    {
        var plan = DiscordCommandBuilder.Build([
            Capability("media", "media.queue", new CommandBinding("queue")),
        ]);

        Assert.Equal("media.queue", plan.RouteToCapabilityKey["media queue"]);

        var root = Assert.Single(plan.Commands);
        var option = Assert.Single(root.Options.Value);
        Assert.Equal(ApplicationCommandOptionType.SubCommand, option.Type);
        Assert.Equal("queue", option.Name);
    }

    [Fact]
    public void Un_chemin_de_deux_segments_cree_un_groupe()
    {
        var plan = DiscordCommandBuilder.Build([
            Capability("hub", "hub.anomaly.snooze", new CommandBinding("anomaly", "snooze")),
        ]);

        Assert.Equal("hub.anomaly.snooze", plan.RouteToCapabilityKey["hub anomaly snooze"]);

        var root = Assert.Single(plan.Commands);
        var group = Assert.Single(root.Options.Value);
        Assert.Equal(ApplicationCommandOptionType.SubCommandGroup, group.Type);
        Assert.Equal("anomaly", group.Name);

        var leaf = Assert.Single(group.Options);
        Assert.Equal(ApplicationCommandOptionType.SubCommand, leaf.Type);
        Assert.Equal("snooze", leaf.Name);
    }

    [Fact]
    public void Deux_capacites_du_meme_groupe_partagent_un_seul_groupe()
    {
        // hub.anomaly.snooze est seul aujourd'hui ; ce test protège la forme générale pour le
        // jour où une seconde capacité rejoindra le même groupe « anomaly ».
        var plan = DiscordCommandBuilder.Build([
            Capability("hub", "hub.anomaly.snooze", new CommandBinding("anomaly", "snooze")),
            Capability("hub", "hub.anomaly.resolve", new CommandBinding("anomaly", "resolve")),
        ]);

        var root = Assert.Single(plan.Commands);
        var group = Assert.Single(root.Options.Value);
        Assert.Equal(2, group.Options.Count);
        Assert.Equal(["resolve", "snooze"], group.Options.Select(o => o.Name).Order());
    }

    [Fact]
    public void Une_capacite_sans_commande_est_exclue()
    {
        // system.backup.create : Exposure.Api seul, jamais de commande (ADR-0004).
        var plan = DiscordCommandBuilder.Build([
            Capability("system", "system.backup.create", command: null),
        ]);

        Assert.Empty(plan.Commands);
        Assert.Empty(plan.RouteToCapabilityKey);
    }

    [Fact]
    public void Les_parametres_se_traduisent_avec_leur_type_leur_obligation_et_leurs_choix()
    {
        var plan = DiscordCommandBuilder.Build([
            Capability("hub", "hub.anomaly.snooze", new CommandBinding("anomaly", "snooze"),
            [
                new CapabilityParameter("key", "Clé", CapabilityParameterType.String, Required: true),
                new CapabilityParameter("hours", "Heures", CapabilityParameterType.Integer, Required: false,
                    Choices: ["6", "24"]),
            ]),
        ]);

        var root = Assert.Single(plan.Commands);
        var leaf = Assert.Single(Assert.Single(root.Options.Value).Options);

        var key = leaf.Options.Single(o => o.Name == "key");
        Assert.Equal(ApplicationCommandOptionType.String, key.Type);
        Assert.True(key.IsRequired);

        var hours = leaf.Options.Single(o => o.Name == "hours");
        Assert.Equal(ApplicationCommandOptionType.Integer, hours.Type);
        Assert.False(hours.IsRequired);
        Assert.Equal(["6", "24"], hours.Choices.Select(c => c.Name));
    }

    [Fact]
    public void Le_pseudo_module_hub_obtient_sa_propre_racine_sans_engloutir_les_autres()
    {
        // Ce que le cadrage a explicitement écarté : un /hub qui contiendrait /media et /system
        // comme sous-commandes. Ici, hub est un module comme les autres, à égalité.
        var plan = DiscordCommandBuilder.Build([
            Capability("hub", "hub.anomaly.snooze", new CommandBinding("anomaly", "snooze")),
            Capability("media", "media.queue", new CommandBinding("queue")),
        ]);

        Assert.Equal(["hub", "media"], plan.Commands.Select(c => c.Name.Value).Order());
        Assert.DoesNotContain(plan.Commands, c => c.Name.Value == "hub"
            && c.Options.Value.Any(o => o.Name == "media"));
    }

    private sealed class StubCapability : IHubCapability
    {
        public CapabilityDescriptor Descriptor => throw new NotSupportedException();

        public Task<CapabilityResult> ExecuteAsync(CapabilityInvocation invocation,
                                                   CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
