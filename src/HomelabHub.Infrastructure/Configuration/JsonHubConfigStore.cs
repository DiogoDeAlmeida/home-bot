using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using HomelabHub.Core.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace HomelabHub.Infrastructure.Configuration;

/// <summary>
/// Configuration persistée dans un fichier JSON, secrets chiffrés via Data Protection.
/// </summary>
/// <remarks>
/// <para>
/// Un fichier plutôt qu'une table : à ce stade la configuration est un dictionnaire clé/valeur
/// sans relation, et SQLite arrivera avec ce qui en a réellement besoin — anomalies, historique,
/// identifiants de messages Discord. La sauvegarde couvre déjà les deux, puisqu'elle archive
/// l'intégralité des répertoires de données et de configuration.
/// </para>
/// <para>
/// L'écriture passe par un fichier temporaire puis un remplacement atomique : une coupure de
/// courant pendant une sauvegarde de configuration ne laisse pas un JSON tronqué.
/// </para>
/// </remarks>
internal sealed class JsonHubConfigStore : IHubConfigStore, IDisposable
{
    private const string EncryptedPrefix = "enc:";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly IDataProtector _protector;
    private readonly HubPlatform _platform;
    private readonly ILogger<JsonHubConfigStore> _logger;

    public JsonHubConfigStore(HubPlatform platform, IDataProtectionProvider dataProtection,
                              ILogger<JsonHubConfigStore> logger)
    {
        _platform = platform;
        _logger = logger;
        _protector = dataProtection.CreateProtector("HomelabHub.Configuration.v1");

        Load();
    }

    public void Dispose() => _writeLock.Dispose();

    public string? GetValue(string key)
    {
        if (!_entries.TryGetValue(key, out var entry) || entry.Value is null)
        {
            return null;
        }

        if (!entry.Secret)
        {
            return entry.Value;
        }

        try
        {
            return entry.Value.StartsWith(EncryptedPrefix, StringComparison.Ordinal)
                ? _protector.Unprotect(entry.Value[EncryptedPrefix.Length..])
                : entry.Value;
        }
        catch (Exception ex)
        {
            // Cas typique : la base a été restaurée sans son keyring. Le message doit dire
            // quoi faire, pas seulement que ça a échoué (ADR-0007).
            _logger.LogError(ex,
                "Déchiffrement impossible pour « {Key} ». Le keyring Data Protection de {Keys} " +
                "ne correspond pas aux données. Restaurer l'archive complète, pas seulement la " +
                "configuration.", key, _platform.KeysDirectory);

            return null;
        }
    }

    public bool IsSecret(string key) => _entries.TryGetValue(key, out var entry) && entry.Secret;

    public IReadOnlyDictionary<string, string> GetByPrefix(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in _entries.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            if (GetValue(key) is { } value)
            {
                result[key] = value;
            }
        }

        return result;
    }

    public Task SetAsync(string key, string? value, bool secret, CancellationToken cancellationToken) =>
        SetManyAsync(new Dictionary<string, ConfigValue> { [key] = new(value, secret) }, cancellationToken);

    public async Task SetManyAsync(IReadOnlyDictionary<string, ConfigValue> values,
                                   CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var (key, entry) in values)
            {
                if (entry.Value is null)
                {
                    _entries.TryRemove(key, out _);
                    continue;
                }

                var stored = entry.Secret
                    ? EncryptedPrefix + _protector.Protect(entry.Value)
                    : entry.Value;

                _entries[key] = new Entry(stored, entry.Secret);
            }

            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void Load()
    {
        var path = _platform.ConfigFilePath;
        if (!File.Exists(path))
        {
            _logger.LogInformation("Aucune configuration existante : {Path} sera créé au premier écrit.", path);
            return;
        }

        try
        {
            var document = JsonSerializer.Deserialize<ConfigDocument>(File.ReadAllText(path), SerializerOptions);
            foreach (var (key, entry) in document?.Entries ?? [])
            {
                _entries[key] = entry;
            }

            _logger.LogInformation("Configuration chargée : {Count} clé(s).", _entries.Count);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Refuser de démarrer plutôt que repartir d'une configuration vide : cela
            // réenclencherait l'assistant de premier démarrage et écraserait le fichier.
            throw new InvalidOperationException(
                $"Configuration illisible : {path}. Restaurer une sauvegarde avant de redémarrer.", ex);
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        var path = _platform.ConfigFilePath;
        var temporary = path + ".tmp";

        var document = new ConfigDocument
        {
            Entries = _entries.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
        };

        await File.WriteAllTextAsync(temporary,
            JsonSerializer.Serialize(document, SerializerOptions), cancellationToken).ConfigureAwait(false);

        RestrictPermissions(temporary);

        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Le fichier contient des secrets chiffrés : inutile de le laisser lisible par tous.</summary>
    private static void RestrictPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private sealed record Entry(string? Value, bool Secret);

    private sealed class ConfigDocument
    {
        public Dictionary<string, Entry> Entries { get; set; } = [];
    }
}
