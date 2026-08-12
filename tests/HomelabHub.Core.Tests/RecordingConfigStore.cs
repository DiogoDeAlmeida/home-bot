using HomelabHub.Core.Configuration;

namespace HomelabHub.Core.Tests;

/// <summary>Un magasin de configuration en mémoire pour les tests qui n'ont besoin que de lire.</summary>
internal sealed class RecordingConfigStore : IHubConfigStore
{
    private readonly Dictionary<string, ConfigValue> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Renseigne une valeur en clair, sans passer par l'écriture asynchrone.</summary>
    public void Set(string key, string value) => _entries[key] = new ConfigValue(value, Secret: false);

    public string? GetValue(string key) =>
        _entries.TryGetValue(key, out var entry) ? entry.Value : null;

    public Task SetAsync(string key, string? value, bool secret, CancellationToken cancellationToken)
    {
        _entries[key] = new ConfigValue(value, secret);
        return Task.CompletedTask;
    }

    public Task SetManyAsync(IReadOnlyDictionary<string, ConfigValue> values, CancellationToken cancellationToken)
    {
        foreach (var (key, value) in values)
        {
            _entries[key] = value;
        }

        return Task.CompletedTask;
    }

    public IReadOnlyDictionary<string, string> GetByPrefix(string prefix) =>
        _entries.Where(e => e.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(e => e.Key, e => e.Value.Value ?? string.Empty);

    public bool IsSecret(string key) => _entries.TryGetValue(key, out var entry) && entry.Secret;
}
