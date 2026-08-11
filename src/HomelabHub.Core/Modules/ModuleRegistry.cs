using HomelabHub.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace HomelabHub.Core.Modules;

/// <summary>
/// État d'activation des modules — la partie « runtime » de ce que le conteneur ne peut pas
/// faire (ADR-0002).
/// </summary>
public interface IModuleRegistry
{
    IReadOnlyList<ModuleDescriptor> Modules { get; }

    /// <summary>Le module est-il opérationnel : activé <b>et</b> correctement configuré ?</summary>
    bool IsActive(string moduleKey);

    /// <summary>État détaillé, pour l'interface web.</summary>
    ModuleActivation GetActivation(string moduleKey);

    /// <summary>Active ou désactive, sans redémarrage.</summary>
    Task SetEnabledAsync(string moduleKey, bool enabled, CancellationToken cancellationToken);
}

/// <param name="Key">Clé du module.</param>
/// <param name="Enabled">Choix de l'utilisateur.</param>
/// <param name="ConfigurationComplete">Tous les champs obligatoires du schéma sont renseignés.</param>
/// <param name="BlockedReason">Pourquoi le module n'est pas actif alors qu'il est activé.</param>
public sealed record ModuleActivation(
    string Key,
    bool Enabled,
    bool ConfigurationComplete,
    string? BlockedReason)
{
    public bool IsActive => Enabled && ConfigurationComplete;
}

internal sealed class ModuleRegistry(
    ModuleCatalog catalog,
    IHubConfigStore store,
    ILogger<ModuleRegistry> logger) : IModuleRegistry
{
    public IReadOnlyList<ModuleDescriptor> Modules => catalog.Descriptors;

    public bool IsActive(string moduleKey) => GetActivation(moduleKey).IsActive;

    public ModuleActivation GetActivation(string moduleKey)
    {
        var descriptor = catalog.Get(moduleKey);

        // Par défaut un module est activé : l'utilisateur n'a pas à cocher une case pour que
        // ce qu'il a compilé fonctionne. C'est la configuration incomplète qui le retient.
        var enabled = store.GetBoolean(EnabledKey(descriptor.Key), fallback: true);

        var missing = descriptor.Module.ConfigSchema.Fields
            .Where(declared => declared.Required)
            .Where(declared => string.IsNullOrWhiteSpace(store.GetValue($"{descriptor.Key}.{declared.Key}")))
            .Select(declared => declared.Label)
            .ToArray();

        var reason = missing.Length > 0
            ? $"Configuration incomplète : {string.Join(", ", missing)}."
            : null;

        return new ModuleActivation(descriptor.Key, enabled, missing.Length == 0, enabled ? reason : null);
    }

    public async Task SetEnabledAsync(string moduleKey, bool enabled, CancellationToken cancellationToken)
    {
        var descriptor = catalog.Get(moduleKey);

        await store.SetAsync(EnabledKey(descriptor.Key),
                             enabled.ToString(),
                             secret: false,
                             cancellationToken).ConfigureAwait(false);

        var key = descriptor.Key;
        var action = enabled ? "activé" : "désactivé";
        logger.LogInformation("Module {Module} {Action}.", key, action);
    }

    private static string EnabledKey(string moduleKey) => $"{moduleKey}.enabled";
}
