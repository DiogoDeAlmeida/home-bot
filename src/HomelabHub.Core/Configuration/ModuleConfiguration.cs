using HomelabHub.Abstractions.Configuration;
using HomelabHub.Core.Modules;

namespace HomelabHub.Core.Configuration;

/// <summary>
/// Vue de la configuration restreinte à un module, sur des clés relatives.
/// </summary>
/// <remarks>
/// Le préfixage est fait ici et nulle part ailleurs : un module ne peut donc pas lire la
/// configuration d'un autre, même par erreur de frappe. Les valeurs par défaut proviennent du
/// <see cref="ModuleConfigSchema"/>, ce qui évite de les répéter dans le code du module.
/// </remarks>
internal sealed class ModuleConfiguration<TModule>(IHubConfigStore store, ModuleCatalog catalog)
    : IModuleConfiguration<TModule>
    where TModule : IHubModuleMarker
{
    private ModuleDescriptor Descriptor => catalog.GetByType(typeof(TModule));

    // « field » est un mot-clé contextuel depuis C# 14 : les variables de lambda sont nommées
    // « declared » pour éviter la collision avec le champ de support synthétisé.
    public bool IsComplete =>
        Descriptor.Module.ConfigSchema.Fields
            .Where(declared => declared.Required)
            .All(declared => !string.IsNullOrWhiteSpace(Resolve(declared.Key)));

    public string? GetString(string key) => Resolve(key);

    public bool GetBoolean(string key, bool fallback = false) =>
        bool.TryParse(Resolve(key), out var parsed) ? parsed : fallback;

    public int GetInt32(string key, int fallback = 0) =>
        int.TryParse(Resolve(key), System.Globalization.NumberStyles.Integer,
                     System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    public TimeSpan GetDuration(string key, TimeSpan fallback) =>
        store.GetDuration(Absolute(key), DefaultDuration(key, fallback));

    private string Absolute(string key) => $"{Descriptor.Key}.{key}";

    /// <summary>Valeur stockée, sinon valeur par défaut du schéma, sinon <c>null</c>.</summary>
    private string? Resolve(string key)
    {
        var stored = store.GetValue(Absolute(key));
        if (!string.IsNullOrWhiteSpace(stored))
        {
            return stored;
        }

        var declared = Descriptor.Module.ConfigSchema.Fields
            .FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

        return declared?.DefaultValue switch
        {
            null => null,
            TimeSpan span => span.ToString("c", System.Globalization.CultureInfo.InvariantCulture),
            var value => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    private TimeSpan DefaultDuration(string key, TimeSpan fallback)
    {
        var declared = Descriptor.Module.ConfigSchema.Fields
            .FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

        return declared?.DefaultValue is TimeSpan span ? span : fallback;
    }
}
