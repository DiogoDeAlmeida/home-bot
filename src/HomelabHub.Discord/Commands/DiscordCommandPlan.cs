using Discord;
using HomelabHub.Core.Capabilities;

namespace HomelabHub.Discord.Commands;

/// <summary>Ce qu'il faut pour enregistrer les commandes en guild, et router les interactions.</summary>
/// <param name="Commands">Une commande racine par module, prête pour l'enregistrement en guild.</param>
/// <param name="RouteToCapabilityKey">
/// Chemin complet tel que Discord le renvoie dans une interaction — <c>"hub anomaly snooze"</c>
/// — vers la clé de capacité correspondante.
/// </param>
internal sealed record DiscordCommandPlan(
    IReadOnlyList<SlashCommandProperties> Commands,
    IReadOnlyDictionary<string, string> RouteToCapabilityKey);

/// <summary>
/// Traduit les capacités découvertes en commandes Discord, sans toucher au réseau.
/// </summary>
/// <remarks>
/// <para>
/// <b>Une racine de commande par module</b> — y compris le pseudo-module <c>hub</c>, qui obtient
/// ainsi son propre <c>/hub</c> pour ses opérations transverses. Ce n'est pas le <c>/hub</c>
/// racine unique que le cadrage avait écarté : celui-là aurait fait de tous les modules des
/// sous-commandes d'une seule commande. Ici, <c>/hub</c> est un module comme les autres, à égalité
/// avec <c>/media</c> et <c>/system</c> — il ne les contient pas.
/// </para>
/// <para>
/// Discord limite un chemin de commande à trois segments : racine, groupe, sous-commande. La clé
/// de module consomme le premier ; <see cref="Abstractions.Capabilities.CommandBinding.Path"/>
/// en couvre au plus deux, contrainte déjà appliquée par <c>CapabilityValidator</c> au démarrage
/// du noyau (ADR-0016 — la migration de cette validation vers l'adaptateur reste due, pas faite
/// ici : le seuil reste juste, dupliqué une fois).
/// </para>
/// </remarks>
internal static class DiscordCommandBuilder
{
    public static DiscordCommandPlan Build(IReadOnlyList<RegisteredCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        var routes = new Dictionary<string, string>(StringComparer.Ordinal);
        var commands = new List<SlashCommandProperties>();

        foreach (var module in capabilities
                     .Where(c => c.Descriptor.Command is not null)
                     .GroupBy(c => c.ModuleKey, StringComparer.OrdinalIgnoreCase))
        {
            commands.Add(BuildRoot(module.Key, [.. module], routes));
        }

        return new DiscordCommandPlan(commands, routes);
    }

    private static SlashCommandProperties BuildRoot(
        string moduleKey, IReadOnlyList<RegisteredCapability> capabilities,
        Dictionary<string, string> routes)
    {
        var root = new SlashCommandBuilder()
            .WithName(moduleKey)
            .WithDescription(RootDescription(moduleKey));

        // Un groupe n'est créé qu'une fois, même si plusieurs capacités y déposent une
        // sous-commande — hub.anomaly.snooze est seul aujourd'hui, mais un second
        // hub.anomaly.something partagerait le même groupe « anomaly ».
        var groups = new Dictionary<string, SlashCommandOptionBuilder>(StringComparer.Ordinal);

        foreach (var registered in capabilities)
        {
            var path = registered.Descriptor.Command!.Path;
            var leaf = BuildSubCommand(path[^1], registered);

            switch (path.Count)
            {
                case 1:
                    root.AddOption(leaf);
                    routes[$"{moduleKey} {path[0]}"] = registered.Descriptor.Key;
                    break;

                case 2:
                    if (!groups.TryGetValue(path[0], out var group))
                    {
                        group = new SlashCommandOptionBuilder()
                            .WithName(path[0])
                            .WithDescription($"Commandes « {path[0]} ».")
                            .WithType(ApplicationCommandOptionType.SubCommandGroup);
                        groups[path[0]] = group;
                        root.AddOption(group);
                    }

                    group.AddOption(leaf);
                    routes[$"{moduleKey} {path[0]} {path[1]}"] = registered.Descriptor.Key;
                    break;

                default:
                    // CapabilityValidator refuse déjà plus de deux segments au démarrage du
                    // noyau : n'atteint ce point que si cette règle a été affaiblie sans que
                    // l'adaptateur suive.
                    throw new NotSupportedException(
                        $"« {registered.Descriptor.Key} » : chemin de commande à {path.Count} " +
                        "segments, non pris en charge par cet adaptateur.");
            }
        }

        return root.Build();
    }

    private static SlashCommandOptionBuilder BuildSubCommand(string name, RegisteredCapability registered)
    {
        var descriptor = registered.Descriptor;

        var option = new SlashCommandOptionBuilder()
            .WithName(name)
            .WithDescription(Truncate(descriptor.Description, 100))
            .WithType(ApplicationCommandOptionType.SubCommand);

        foreach (var parameter in descriptor.Parameters)
        {
            var built = new SlashCommandOptionBuilder()
                .WithName(parameter.Name)
                .WithDescription(Truncate(parameter.Description, 100))
                .WithType(ToOptionType(parameter.Type))
                .WithRequired(parameter.Required);

            if (parameter.Choices is { Count: > 0 })
            {
                foreach (var choice in parameter.Choices)
                {
                    built.AddChoice(choice, choice);
                }
            }

            option.AddOption(built);
        }

        return option;
    }

    private static string RootDescription(string moduleKey) =>
        string.Equals(moduleKey, HomelabHub.Core.Configuration.HubSettings.Prefix, StringComparison.Ordinal)
            ? "Opérations du noyau, transverses aux modules."
            : $"Commandes du module {moduleKey}.";

    private static ApplicationCommandOptionType ToOptionType(
        Abstractions.Capabilities.CapabilityParameterType type) => type switch
    {
        Abstractions.Capabilities.CapabilityParameterType.String => ApplicationCommandOptionType.String,
        Abstractions.Capabilities.CapabilityParameterType.Integer => ApplicationCommandOptionType.Integer,
        Abstractions.Capabilities.CapabilityParameterType.Number => ApplicationCommandOptionType.Number,
        Abstractions.Capabilities.CapabilityParameterType.Boolean => ApplicationCommandOptionType.Boolean,
        _ => throw new NotSupportedException($"Type de paramètre non pris en charge : {type}."),
    };

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
