namespace HomelabHub.Core.Modules;

/// <summary>
/// Catalogue immuable des modules, construit au démarrage avant même que le conteneur existe.
/// </summary>
public sealed class ModuleCatalog
{
    private readonly Dictionary<string, ModuleDescriptor> _byKey;
    private readonly Dictionary<Type, ModuleDescriptor> _byType;

    internal ModuleCatalog(IReadOnlyList<ModuleDescriptor> descriptors)
    {
        Descriptors = descriptors;
        _byKey = descriptors.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);
        _byType = descriptors.ToDictionary(d => d.ModuleType);
    }

    public IReadOnlyList<ModuleDescriptor> Descriptors { get; }

    public ModuleDescriptor? Find(string moduleKey) =>
        _byKey.GetValueOrDefault(moduleKey);

    public ModuleDescriptor Get(string moduleKey) =>
        Find(moduleKey) ?? throw new HubConfigurationException($"Module inconnu : « {moduleKey} ».");

    public ModuleDescriptor GetByType(Type moduleType) =>
        _byType.TryGetValue(moduleType, out var descriptor)
            ? descriptor
            : throw new HubConfigurationException(
                $"Le type {moduleType.Name} n'est pas un module enregistré.");
}
