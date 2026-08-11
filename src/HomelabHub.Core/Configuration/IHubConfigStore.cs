using System.Globalization;

namespace HomelabHub.Core.Configuration;

/// <summary>
/// Magasin de configuration du hub : clés absolues, valeurs chaînes, secrets chiffrés au repos.
/// </summary>
/// <remarks>
/// <para>
/// Les clés sont absolues et préfixées par le module (<c>media.radarr.apiKey</c>). Les modules
/// n'utilisent pas cette interface : ils passent par
/// <see cref="Abstractions.Configuration.IModuleConfiguration{TModule}"/>, qui travaille sur des
/// clés relatives et ne peut pas lire la configuration d'un autre module.
/// </para>
/// <para>
/// Les valeurs marquées secrètes sont chiffrées via Data Protection. Le keyring vit dans le
/// répertoire de données et <b>doit être sauvegardé avec la base</b> — d'où l'archive unique
/// (ADR-0007).
/// </para>
/// </remarks>
public interface IHubConfigStore
{
    /// <summary>Valeur brute, déchiffrée si nécessaire, ou <c>null</c> si absente.</summary>
    string? GetValue(string key);

    /// <summary>Écrit une valeur. <paramref name="value"/> à <c>null</c> supprime la clé.</summary>
    Task SetAsync(string key, string? value, bool secret, CancellationToken cancellationToken);

    /// <summary>Écrit plusieurs valeurs en une seule persistance.</summary>
    Task SetManyAsync(IReadOnlyDictionary<string, ConfigValue> values, CancellationToken cancellationToken);

    /// <summary>Clés connues commençant par <paramref name="prefix"/>, valeurs déchiffrées.</summary>
    IReadOnlyDictionary<string, string> GetByPrefix(string prefix);

    /// <summary>Indique si une clé est marquée secrète — l'API doit alors masquer sa valeur.</summary>
    bool IsSecret(string key);
}

/// <param name="Value">Valeur à écrire, ou <c>null</c> pour supprimer la clé.</param>
/// <param name="Secret">Chiffrer au repos et masquer en lecture.</param>
public readonly record struct ConfigValue(string? Value, bool Secret);

/// <summary>Lectures typées, partagées par toutes les implémentations.</summary>
public static class HubConfigStoreExtensions
{
    public static bool GetBoolean(this IHubConfigStore store, string key, bool fallback = false)
    {
        ArgumentNullException.ThrowIfNull(store);
        return bool.TryParse(store.GetValue(key), out var parsed) ? parsed : fallback;
    }

    public static int GetInt32(this IHubConfigStore store, string key, int fallback = 0)
    {
        ArgumentNullException.ThrowIfNull(store);
        return int.TryParse(store.GetValue(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    /// <summary>
    /// Durée, acceptée soit en secondes (<c>90</c>), soit au format <c>TimeSpan</c>
    /// (<c>00:01:30</c>). Le formulaire web produit des secondes ; une valeur par défaut
    /// déclarée au schéma est sérialisée en <c>TimeSpan</c>.
    /// </summary>
    /// <remarks>
    /// L'ordre d'interprétation n'est pas anodin : <c>TimeSpan.TryParse("90")</c> réussit et
    /// rend <b>90 jours</b>. Un intervalle de polling saisi « 90 » deviendrait trimestriel, en
    /// silence. La présence d'un <c>:</c> est donc le seul critère qui distingue les deux formats.
    /// </remarks>
    public static TimeSpan GetDuration(this IHubConfigStore store, string key, TimeSpan fallback)
    {
        ArgumentNullException.ThrowIfNull(store);

        var raw = store.GetValue(key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (raw.Contains(':', StringComparison.Ordinal))
        {
            return TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var span) && span > TimeSpan.Zero
                ? span
                : fallback;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
               && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : fallback;
    }
}
