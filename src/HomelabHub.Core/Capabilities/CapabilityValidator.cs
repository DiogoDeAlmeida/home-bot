using System.Text.RegularExpressions;
using HomelabHub.Abstractions.Capabilities;

namespace HomelabHub.Core.Capabilities;

/// <summary>
/// Vérifie qu'une capacité est déclarée de façon cohérente, et qu'elle tient dans les
/// contraintes de Discord si elle prétend s'y exposer.
/// </summary>
/// <remarks>
/// Ces contrôles s'exécutent au démarrage et font échouer le processus. C'est délibéré : une
/// commande Discord silencieusement absente parce que sa description faisait 103 caractères
/// est le genre de bug qu'on cherche une heure. Autant l'apprendre au premier lancement.
/// </remarks>
public static partial class CapabilityValidator
{
    /// <summary>Limite Discord sur la longueur d'une description de commande ou d'option.</summary>
    private const int DiscordDescriptionMaxLength = 100;

    /// <summary>Limite Discord sur le nombre d'options d'une sous-commande.</summary>
    private const int DiscordMaxOptions = 25;

    /// <summary>Limite Discord sur le nombre de choix d'une option.</summary>
    private const int DiscordMaxChoices = 25;

    public static IReadOnlyList<string> Validate(string moduleKey, CapabilityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var errors = new List<string>();
        var name = descriptor.Key;

        if (!descriptor.Key.StartsWith($"{moduleKey}.", StringComparison.Ordinal))
        {
            errors.Add($"« {name} » : la clé doit être préfixée par « {moduleKey}. ».");
        }

        if (descriptor.Exposure == CapabilityExposure.None)
        {
            errors.Add($"« {name} » : exposition None — la capacité serait injoignable.");
        }

        // La contradiction la plus dangereuse : une capacité restreinte au REST qui déclare
        // malgré tout une commande Discord. Refuser plutôt que d'arbitrer en silence.
        if (descriptor.Discord is not null && !descriptor.Exposure.HasFlag(CapabilityExposure.Discord))
        {
            errors.Add($"« {name} » : un DiscordBinding est déclaré alors que l'exposition " +
                       "exclut Discord. Retirer le binding, ou élargir l'exposition.");
        }

        if (descriptor.Parameters.Count > DiscordMaxOptions)
        {
            errors.Add($"« {name} » : {descriptor.Parameters.Count} paramètres, maximum {DiscordMaxOptions}.");
        }

        foreach (var duplicate in descriptor.Parameters
                     .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            errors.Add($"« {name} » : paramètre « {duplicate.Key} » déclaré plusieurs fois.");
        }

        // Un paramètre optionnel suivi d'un obligatoire est refusé par l'API Discord.
        var seenOptional = false;
        foreach (var parameter in descriptor.Parameters)
        {
            if (!parameter.Required)
            {
                seenOptional = true;
            }
            else if (seenOptional)
            {
                errors.Add($"« {name} » : le paramètre obligatoire « {parameter.Name} » suit un " +
                           "paramètre optionnel ; Discord impose l'ordre inverse.");
                break;
            }
        }

        if (descriptor.Discord is { } binding)
        {
            ValidateDiscordBinding(name, descriptor, binding, errors);
        }

        return errors;
    }

    private static void ValidateDiscordBinding(string name, CapabilityDescriptor descriptor,
                                               DiscordBinding binding, List<string> errors)
    {
        if (!CommandNamePattern().IsMatch(binding.Name))
        {
            errors.Add($"« {name} » : nom de sous-commande « {binding.Name} » invalide " +
                       "(minuscules, chiffres, tiret et souligné, 1 à 32 caractères).");
        }

        if (binding.SubGroup is not null && !CommandNamePattern().IsMatch(binding.SubGroup))
        {
            errors.Add($"« {name} » : nom de groupe « {binding.SubGroup} » invalide.");
        }

        if (descriptor.Description.Length > DiscordDescriptionMaxLength)
        {
            errors.Add($"« {name} » : description de {descriptor.Description.Length} caractères, " +
                       $"maximum {DiscordDescriptionMaxLength} pour une commande Discord.");
        }

        foreach (var parameter in descriptor.Parameters)
        {
            if (!CommandNamePattern().IsMatch(parameter.Name))
            {
                errors.Add($"« {name} » : nom de paramètre « {parameter.Name} » invalide.");
            }

            if (parameter.Description.Length > DiscordDescriptionMaxLength)
            {
                errors.Add($"« {name} » : description du paramètre « {parameter.Name} » trop longue.");
            }

            if (parameter.Choices is { Count: > DiscordMaxChoices })
            {
                errors.Add($"« {name} » : paramètre « {parameter.Name} » a {parameter.Choices.Count} " +
                           $"choix, maximum {DiscordMaxChoices}.");
            }
        }
    }

    [GeneratedRegex("^[a-z0-9_-]{1,32}$")]
    private static partial Regex CommandNamePattern();
}
