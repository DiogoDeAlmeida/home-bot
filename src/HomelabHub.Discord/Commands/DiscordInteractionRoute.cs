using Discord;
using Discord.WebSocket;

namespace HomelabHub.Discord.Commands;

/// <summary>
/// Une option d'interaction, découplée du type concret de Discord.Net.
/// </summary>
/// <remarks>
/// <see cref="SocketSlashCommandDataOption"/> n'a ni constructeur ni accesseur public
/// exploitables hors de son assembly — impossible à construire dans un test sans passerelle
/// réelle. Cette structure porte la même forme, mais reste à la charge de l'appelant : voir
/// <see cref="FromDiscord"/> pour le seul endroit qui fait la conversion.
/// </remarks>
internal readonly record struct InteractionOption(
    string Name,
    ApplicationCommandOptionType Type,
    object? Value,
    IReadOnlyList<InteractionOption> Options)
{
    public static InteractionOption FromDiscord(SocketSlashCommandDataOption option)
    {
        ArgumentNullException.ThrowIfNull(option);

        return new InteractionOption(option.Name, option.Type, option.Value,
            [.. option.Options.Select(FromDiscord)]);
    }
}

/// <summary>
/// Reconstruit, à partir d'une interaction reçue, exactement ce que
/// <see cref="DiscordCommandBuilder"/> a mis dans <c>RouteToCapabilityKey</c> en la construisant.
/// </summary>
/// <remarks>
/// Les deux formes de chemin — <c>racine sous-commande</c> et <c>racine groupe sous-commande</c>
/// — se distinguent par la profondeur d'imbrication des options ; ni l'une ni l'autre n'est
/// privilégiée, la boucle descend jusqu'à trouver la feuille, quelle que soit sa profondeur.
/// </remarks>
internal static class DiscordInteractionRoute
{
    public static (string Route, IReadOnlyDictionary<string, object?> Arguments) Read(
        string rootName, IReadOnlyCollection<InteractionOption>? options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootName);

        var segments = new List<string> { rootName };
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        var cursor = options;

        while (cursor is { Count: > 0 })
        {
            var next = cursor.FirstOrDefault(o => o.Type
                is ApplicationCommandOptionType.SubCommand or ApplicationCommandOptionType.SubCommandGroup);

            if (next.Name is null)
            {
                // Plus de sous-commande ni de groupe à descendre : ce niveau porte les
                // paramètres réels de la feuille déjà atteinte.
                foreach (var option in cursor)
                {
                    arguments[option.Name] = option.Value;
                }

                break;
            }

            segments.Add(next.Name);
            cursor = next.Options;
        }

        return (string.Join(' ', segments), arguments);
    }
}
