using System.Globalization;
using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Core.Modules;
using Microsoft.Extensions.Logging;

namespace HomelabHub.Core.Capabilities;

/// <summary>
/// Point de passage unique pour exécuter une capacité, quelle que soit la surface d'appel.
/// </summary>
/// <remarks>
/// <b>C'est ici, et nulle part ailleurs, que se décide l'autorisation</b> (ADR-0004). Les
/// plateformes conversationnelles ne savent en général pas donner de permission à une
/// sous-commande, et n'en donnent aucune aux boutons : la seule protection réelle est cette
/// vérification. La dupliquer dans un adaptateur créerait une seconde source de vérité qui
/// finirait par diverger — et il faudrait la réécrire à chaque adaptateur ajouté (ADR-0016).
/// </remarks>
public interface ICapabilityExecutor
{
    Task<CapabilityResult> ExecuteAsync(CapabilityInvocation invocation, CancellationToken cancellationToken);
}

internal sealed class CapabilityExecutor(
    ICapabilityRegistry registry,
    IModuleRegistry modules,
    ILogger<CapabilityExecutor> logger) : ICapabilityExecutor
{
    public async Task<CapabilityResult> ExecuteAsync(CapabilityInvocation invocation,
                                                     CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var registered = registry.Find(invocation.CapabilityKey);
        if (registered is null)
        {
            return CapabilityResult.Fail($"Capacité inconnue : « {invocation.CapabilityKey} ».");
        }

        var descriptor = registered.Descriptor;

        var activation = modules.GetActivation(registered.ModuleKey);
        if (!activation.IsActive)
        {
            return CapabilityResult.Fail(
                activation.BlockedReason ?? $"Le module « {registered.ModuleKey} » est désactivé.");
        }

        if (!IsExposedTo(descriptor.Exposure, invocation.Source))
        {
            logger.LogWarning(
                "Appel de {Capability} refusé : la surface {Source} n'est pas autorisée.",
                descriptor.Key, invocation.Source);

            return CapabilityResult.Fail("Cette opération n'est pas disponible depuis cette interface.");
        }

        if (descriptor.Kind == CapabilityKind.Mutation && !invocation.IsAdministrator)
        {
            logger.LogWarning("Appel de {Capability} refusé pour {Actor} : rôle insuffisant.",
                descriptor.Key, invocation.ActorId);

            return CapabilityResult.Fail("Opération réservée aux administrateurs du hub.");
        }

        if (!TryBindArguments(descriptor, invocation.Arguments, out var arguments, out var bindingError))
        {
            return CapabilityResult.Fail(bindingError);
        }

        var bound = invocation with { Arguments = arguments };

        try
        {
            var result = await registered.Capability.ExecuteAsync(bound, cancellationToken)
                                         .ConfigureAwait(false);

            if (descriptor.Kind == CapabilityKind.Mutation)
            {
                logger.LogInformation("{Actor} a exécuté {Capability} depuis {Source} : {Outcome}.",
                    invocation.ActorId, descriptor.Key, invocation.Source, result.Outcome);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Convention §14 : une capacité qui échoue dégrade la réponse, elle ne fait pas
            // tomber le hub ni remonter une pile d'appels à l'utilisateur.
            logger.LogError(ex, "Échec de la capacité {Capability}.", descriptor.Key);
            return CapabilityResult.Fail("L'opération a échoué. Voir le journal pour le détail.");
        }
    }

    private static bool IsExposedTo(CapabilityExposure exposure, InvocationSource source) => source switch
    {
        InvocationSource.Api => exposure.HasFlag(CapabilityExposure.Api),
        InvocationSource.ChatCommand or InvocationSource.ChatButton =>
            exposure.HasFlag(CapabilityExposure.Chat),
        InvocationSource.Internal => true,
        _ => false,
    };

    /// <summary>
    /// Valide et convertit les arguments contre le descripteur. Les arguments non déclarés sont
    /// ignorés : un adaptateur ne doit pas pouvoir injecter ce que la capacité n'attend pas.
    /// </summary>
    private static bool TryBindArguments(CapabilityDescriptor descriptor,
                                         IReadOnlyDictionary<string, object?> supplied,
                                         out IReadOnlyDictionary<string, object?> bound,
                                         out string error)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in descriptor.Parameters)
        {
            supplied.TryGetValue(parameter.Name, out var raw);

            if (raw is null || (raw is string s && string.IsNullOrWhiteSpace(s)))
            {
                if (parameter.Required)
                {
                    bound = result;
                    error = $"Le paramètre « {parameter.Name} » est obligatoire.";
                    return false;
                }

                if (parameter.DefaultValue is not null)
                {
                    result[parameter.Name] = parameter.DefaultValue;
                }

                continue;
            }

            if (!TryConvert(raw, parameter.Type, out var converted))
            {
                bound = result;
                error = $"Le paramètre « {parameter.Name} » attend une valeur de type {parameter.Type}.";
                return false;
            }

            if (parameter.Choices is { Count: > 0 })
            {
                var text = Convert.ToString(converted, CultureInfo.InvariantCulture);
                if (!parameter.Choices.Contains(text, StringComparer.OrdinalIgnoreCase))
                {
                    bound = result;
                    error = $"Valeur invalide pour « {parameter.Name} ». " +
                            $"Attendu : {string.Join(", ", parameter.Choices)}.";
                    return false;
                }
            }

            result[parameter.Name] = converted;
        }

        bound = result;
        error = string.Empty;
        return true;
    }

    private static bool TryConvert(object raw, CapabilityParameterType type, out object? converted)
    {
        var text = Convert.ToString(raw, CultureInfo.InvariantCulture);

        switch (type)
        {
            case CapabilityParameterType.String:
                converted = text;
                return true;

            case CapabilityParameterType.Integer:
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                {
                    converted = i;
                    return true;
                }

                break;

            case CapabilityParameterType.Number:
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                {
                    converted = d;
                    return true;
                }

                break;

            case CapabilityParameterType.Boolean:
                if (bool.TryParse(text, out var b))
                {
                    converted = b;
                    return true;
                }

                break;

            default:
                break;
        }

        converted = null;
        return false;
    }
}
