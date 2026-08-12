using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Core.Configuration;
using HomelabHub.Core.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace HomelabHub.Core.Capabilities;

/// <summary>Index des capacités déclarées par tous les modules, validé au démarrage.</summary>
public interface ICapabilityRegistry
{
    IReadOnlyList<RegisteredCapability> All { get; }

    RegisteredCapability? Find(string capabilityKey);

    /// <summary>Capacités d'un module, filtrées sur une surface d'exposition.</summary>
    IReadOnlyList<RegisteredCapability> ForModule(string moduleKey, CapabilityExposure exposure);
}

/// <param name="ModuleKey">Module propriétaire.</param>
/// <param name="Descriptor">Description statique.</param>
/// <param name="Capability">Instance résolue depuis le conteneur.</param>
public sealed record RegisteredCapability(
    string ModuleKey,
    CapabilityDescriptor Descriptor,
    IHubCapability Capability);

internal sealed class CapabilityRegistry : ICapabilityRegistry
{
    private readonly Dictionary<string, RegisteredCapability> _byKey;

    public CapabilityRegistry(ModuleCatalog catalog, HubCapabilityCatalog hubCapabilities,
                              IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(hubCapabilities);
        ArgumentNullException.ThrowIfNull(services);

        var errors = new List<string>();
        var registered = new List<RegisteredCapability>();

        foreach (var module in catalog.Descriptors)
        {
            foreach (var type in module.CapabilityTypes)
            {
                var capability = (IHubCapability)services.GetRequiredService(type);
                var descriptor = capability.Descriptor;

                errors.AddRange(CapabilityValidator.Validate(module.Key, descriptor));
                registered.Add(new RegisteredCapability(module.Key, descriptor, capability));
            }
        }

        // Capacités du noyau : même validation, sous la clé réservée « hub » — le noyau est un
        // pseudo-module aux yeux du validateur, comme il l'est déjà pour la configuration
        // (ADR-0013, HubCapabilityCatalog).
        foreach (var type in hubCapabilities.Types)
        {
            var capability = (IHubCapability)services.GetRequiredService(type);
            var descriptor = capability.Descriptor;

            errors.AddRange(CapabilityValidator.Validate(HubSettings.Prefix, descriptor));
            registered.Add(new RegisteredCapability(HubSettings.Prefix, descriptor, capability));
        }

        foreach (var duplicate in registered
                     .GroupBy(c => c.Descriptor.Key, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            errors.Add($"Clé de capacité « {duplicate.Key} » déclarée {duplicate.Count()} fois.");
        }

        // Deux capacités du même module ne peuvent pas revendiquer le même chemin de commande.
        foreach (var duplicate in registered
                     .Where(c => c.Descriptor.Command is not null)
                     .GroupBy(c => $"{c.ModuleKey} {string.Join(' ', c.Descriptor.Command!.Path)}",
                              StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            errors.Add($"Commande « {duplicate.Key} » revendiquée par plusieurs capacités.");
        }

        if (errors.Count > 0)
        {
            throw HubConfigurationException.FromErrors("Déclaration de capacités invalide", errors);
        }

        All = registered;
        _byKey = registered.ToDictionary(c => c.Descriptor.Key, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<RegisteredCapability> All { get; }

    public RegisteredCapability? Find(string capabilityKey) => _byKey.GetValueOrDefault(capabilityKey);

    public IReadOnlyList<RegisteredCapability> ForModule(string moduleKey, CapabilityExposure exposure) =>
        [.. All.Where(c => string.Equals(c.ModuleKey, moduleKey, StringComparison.OrdinalIgnoreCase)
                           && c.Descriptor.Exposure.HasFlag(exposure))];
}
