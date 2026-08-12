using Discord;
using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Core.Capabilities;
using HomelabHub.Core.Configuration;
using HomelabHub.Discord.Commands;
using Xunit;

namespace HomelabHub.Discord.Tests;

/// <summary>
/// Reconstruction du chemin et des arguments depuis une interaction reçue — le pendant, à
/// l'exécution, de ce que <see cref="DiscordCommandBuilder"/> construit au démarrage.
/// </summary>
public sealed class DiscordInteractionRouteTests
{
    private static InteractionOption Option(
        string name, ApplicationCommandOptionType type, object? value = null,
        IReadOnlyList<InteractionOption>? options = null) =>
        new(name, type, value, options ?? []);

    [Fact]
    public void Une_sous_commande_directe_sans_parametre()
    {
        var (route, arguments) = DiscordInteractionRoute.Read("media",
            [Option("queue", ApplicationCommandOptionType.SubCommand)]);

        Assert.Equal("media queue", route);
        Assert.Empty(arguments);
    }

    [Fact]
    public void Une_sous_commande_directe_avec_parametre()
    {
        var (route, arguments) = DiscordInteractionRoute.Read("media",
        [
            Option("import", ApplicationCommandOptionType.SubCommand, options:
            [
                Option("download", ApplicationCommandOptionType.String, value: "481b6e36"),
            ]),
        ]);

        Assert.Equal("media import", route);
        Assert.Equal("481b6e36", arguments["download"]);
    }

    [Fact]
    public void Un_groupe_et_sa_sous_commande_avec_deux_parametres()
    {
        var (route, arguments) = DiscordInteractionRoute.Read("hub",
        [
            Option("anomaly", ApplicationCommandOptionType.SubCommandGroup, options:
            [
                Option("snooze", ApplicationCommandOptionType.SubCommand, options:
                [
                    Option("key", ApplicationCommandOptionType.String, value: "media.import.pending:aa"),
                    Option("hours", ApplicationCommandOptionType.Integer, value: 6L),
                ]),
            ]),
        ]);

        Assert.Equal("hub anomaly snooze", route);
        Assert.Equal("media.import.pending:aa", arguments["key"]);
        Assert.Equal(6L, arguments["hours"]);
    }

    [Fact]
    public void Une_racine_sans_options_ne_produit_que_la_racine()
    {
        var (route, arguments) = DiscordInteractionRoute.Read("media", options: null);

        Assert.Equal("media", route);
        Assert.Empty(arguments);
    }

    [Fact]
    public void Le_chemin_construit_correspond_exactement_a_la_route_du_constructeur()
    {
        // Le test qui compte vraiment : les deux moitiés du même contrat, l'une qui construit la
        // commande, l'autre qui la reconnaît, doivent produire la même chaîne pour la même clé.
        var plan = DiscordCommandBuilder.Build([
            new RegisteredCapability(HubSettings.Prefix,
                new CapabilityDescriptor(
                    "hub.anomaly.snooze", "Snooze", "Description.", [],
                    CapabilityKind.Mutation, CapabilityExposure.All,
                    new CommandBinding("anomaly", "snooze")),
                new NoopCapability()),
        ]);

        var (route, _) = DiscordInteractionRoute.Read("hub",
        [
            Option("anomaly", ApplicationCommandOptionType.SubCommandGroup, options:
            [
                Option("snooze", ApplicationCommandOptionType.SubCommand),
            ]),
        ]);

        Assert.True(plan.RouteToCapabilityKey.ContainsKey(route));
        Assert.Equal("hub.anomaly.snooze", plan.RouteToCapabilityKey[route]);
    }

    private sealed class NoopCapability : IHubCapability
    {
        public CapabilityDescriptor Descriptor => throw new NotSupportedException();

        public Task<CapabilityResult> ExecuteAsync(CapabilityInvocation invocation,
                                                   CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
