using System.Text.RegularExpressions;
using HomelabHub.Abstractions.Capabilities;

namespace HomelabHub.Core.Capabilities;

/// <summary>
/// Vérifie qu'une capacité est déclarée de façon cohérente et qu'elle tient dans les contraintes
/// communes aux canaux conversationnels.
/// </summary>
/// <remarks>
/// <para>
/// Ces contrôles s'exécutent au démarrage et font échouer le processus. C'est délibéré : une
/// commande silencieusement absente parce que sa description faisait 103 caractères est le genre
/// de bug qu'on cherche une heure.
/// </para>
/// <para>
/// <b>Partage des responsabilités (ADR-0016).</b> Le noyau valide ce qui est générique : forme
/// des noms, unicité, cohérence entre exposition et commande, ordre des paramètres. Les limites
/// propres à une plateforme — profondeur de commande, quotas — appartiennent à son adaptateur et
/// seront vérifiées par lui. Les seuils ci-dessous encodent la contrainte la plus stricte connue
/// à ce jour ; ils migreront dans l'adaptateur Discord quand il sera écrit.
/// </para>
/// </remarks>
public static partial class CapabilityValidator
{
    /// <summary>Longueur maximale d'une description exposée comme commande.</summary>
    private const int DescriptionMaxLength = 100;

    /// <summary>Nombre maximal de paramètres d'une commande.</summary>
    private const int MaxParameters = 25;

    /// <summary>Nombre maximal de choix d'un paramètre.</summary>
    private const int MaxChoices = 25;

    /// <summary>
    /// Profondeur maximale d'un chemin de commande, hors clé de module.
    /// </summary>
    /// <remarks>
    /// Discord plafonne à trois niveaux au total — commande, groupe, sous-commande. La clé du
    /// module consomme le premier, il en reste deux. Contrainte à déplacer dans l'adaptateur
    /// Discord à l'étape 3 (ADR-0016).
    /// </remarks>
    private const int MaxCommandDepth = 2;

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

        // La contradiction la plus dangereuse : une capacité restreinte à l'API qui déclare
        // malgré tout une commande. Refuser plutôt qu'arbitrer en silence.
        if (descriptor.Command is not null && !descriptor.Exposure.HasFlag(CapabilityExposure.Chat))
        {
            errors.Add($"« {name} » : une commande est déclarée alors que l'exposition exclut les " +
                       "canaux conversationnels. Retirer la commande, ou élargir l'exposition.");
        }

        if (descriptor.Parameters.Count > MaxParameters)
        {
            errors.Add($"« {name} » : {descriptor.Parameters.Count} paramètres, maximum {MaxParameters}.");
        }

        foreach (var duplicate in descriptor.Parameters
                     .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            errors.Add($"« {name} » : paramètre « {duplicate.Key} » déclaré plusieurs fois.");
        }

        // Un paramètre optionnel suivi d'un obligatoire est refusé par la plupart des API.
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
                           "paramètre optionnel ; l'ordre inverse est imposé.");
                break;
            }
        }

        if (descriptor.Command is { } command)
        {
            ValidateCommand(name, descriptor, command, errors);
        }

        return errors;
    }

    private static void ValidateCommand(string name, CapabilityDescriptor descriptor,
                                        CommandBinding command, List<string> errors)
    {
        if (command.Path.Count == 0)
        {
            errors.Add($"« {name} » : chemin de commande vide.");
        }

        if (command.Path.Count > MaxCommandDepth)
        {
            errors.Add($"« {name} » : chemin de commande à {command.Path.Count} segments, " +
                       $"maximum {MaxCommandDepth} hors clé de module.");
        }

        foreach (var segment in command.Path)
        {
            if (!CommandNamePattern().IsMatch(segment))
            {
                errors.Add($"« {name} » : segment de commande « {segment} » invalide " +
                           "(minuscules, chiffres, tiret et souligné, 1 à 32 caractères).");
            }
        }

        if (descriptor.Description.Length > DescriptionMaxLength)
        {
            errors.Add($"« {name} » : description de {descriptor.Description.Length} caractères, " +
                       $"maximum {DescriptionMaxLength} pour une commande.");
        }

        foreach (var parameter in descriptor.Parameters)
        {
            if (!CommandNamePattern().IsMatch(parameter.Name))
            {
                errors.Add($"« {name} » : nom de paramètre « {parameter.Name} » invalide.");
            }

            if (parameter.Description.Length > DescriptionMaxLength)
            {
                errors.Add($"« {name} » : description du paramètre « {parameter.Name} » trop longue.");
            }

            if (parameter.Choices is { Count: > MaxChoices })
            {
                errors.Add($"« {name} » : paramètre « {parameter.Name} » a {parameter.Choices.Count} " +
                           $"choix, maximum {MaxChoices}.");
            }
        }
    }

    [GeneratedRegex("^[a-z0-9_-]{1,32}$")]
    private static partial Regex CommandNamePattern();
}
