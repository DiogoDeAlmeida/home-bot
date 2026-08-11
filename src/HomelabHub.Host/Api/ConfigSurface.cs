using HomelabHub.Abstractions.Configuration;
using HomelabHub.Core.Configuration;

namespace HomelabHub.Host.Api;

/// <summary>
/// Projection et écriture d'un schéma de configuration, quelle que soit son origine.
/// </summary>
/// <remarks>
/// Utilisé à l'identique par <c>/api/modules/{clé}/config</c> et par <c>/api/settings</c>
/// (ADR-0013). Seul le préfixe de clé change. C'est ce qui garantit qu'un seul générateur de
/// formulaire React suffit : si les deux surfaces divergeaient ici, elles divergeraient aussi
/// dans l'interface.
/// </remarks>
internal static class ConfigSurface
{
    /// <summary>Masque appliqué aux secrets renvoyés par l'API.</summary>
    private const char MaskCharacter = '•';

    public static object Describe(string prefix, IReadOnlyList<ConfigField> fields, IHubConfigStore store) =>
        new
        {
            key = prefix,
            fields = fields.Select(declared => new
            {
                declared.Key,
                declared.Label,
                kind = declared.Kind.ToString(),
                declared.Required,
                declared.Secret,
                declared.Help,
                defaultValue = Stringify(declared.DefaultValue),
                declared.Options,
                // Présent dans le contrat, non résolu en v1 : le front rend une saisie libre
                // tant qu'aucun module n'en a réellement besoin (ADR-0011).
                declared.OptionsFrom,
                declared.DependsOn,
                value = ReadForDisplay(store, prefix, declared),
            }),
        };

    public static async Task<IResult> WriteAsync(string prefix, IReadOnlyList<ConfigField> fields,
                                                 IReadOnlyDictionary<string, string?> values,
                                                 IHubConfigStore store,
                                                 CancellationToken cancellationToken)
    {
        var schema = fields.ToDictionary(declared => declared.Key, StringComparer.OrdinalIgnoreCase);
        var writes = new Dictionary<string, ConfigValue>(StringComparer.OrdinalIgnoreCase);
        var rejected = new List<string>();

        foreach (var (field, value) in values)
        {
            if (!schema.TryGetValue(field, out var declared))
            {
                // Le formulaire est généré depuis le schéma : une clé inconnue est soit une
                // faute de frappe, soit un abus. Dans les deux cas, on refuse.
                rejected.Add(field);
                continue;
            }

            // Un secret réaffiché masqué puis renvoyé tel quel ne doit pas écraser la vraie
            // valeur : c'est le piège classique du formulaire en écriture seule.
            if (declared.Secret && value is { Length: > 0 } && value.All(c => c == MaskCharacter))
            {
                continue;
            }

            writes[$"{prefix}.{declared.Key}"] = new ConfigValue(value, declared.Secret);
        }

        if (rejected.Count > 0)
        {
            return Results.BadRequest(new { error = "unknown_fields", fields = rejected });
        }

        await store.SetManyAsync(writes, cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    /// <summary>Un secret ne repart jamais en clair de l'API : écriture seule, lecture masquée.</summary>
    private static string? ReadForDisplay(IHubConfigStore store, string prefix, ConfigField declared)
    {
        var value = store.GetValue($"{prefix}.{declared.Key}");

        if (!declared.Secret || string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= 4
            ? new string(MaskCharacter, 8)
            : new string(MaskCharacter, 6) + value[^4..];
    }

    /// <summary>
    /// Les valeurs par défaut partent en chaînes, comme les valeurs saisies : le formulaire
    /// n'a pas à distinguer un défaut typé d'une saisie utilisateur.
    /// </summary>
    private static string? Stringify(object? value) => value switch
    {
        null => null,
        TimeSpan span => ((int)span.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
    };
}
