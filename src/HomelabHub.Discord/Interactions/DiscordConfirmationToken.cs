using System.Globalization;

namespace HomelabHub.Discord.Interactions;

/// <summary>
/// Encode une invocation de capacité dans un <c>custom_id</c> de bouton, et la décode au clic.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sans état côté serveur, délibérément.</b> Le <c>custom_id</c> porte tout ce qu'il faut
/// pour rejouer l'appel — clé de capacité et arguments — plutôt qu'une référence vers une
/// session gardée en mémoire. Un redémarrage du hub entre l'affichage du bouton et le clic ne
/// casse donc rien : la confirmation d'un import déclenché juste avant un redéploiement reste
/// valide.
/// </para>
/// <para>
/// <b>Sans secret</b> : les arguments des capacités confirmables sont des identifiants
/// techniques (hash de téléchargement, clé d'anomalie), jamais une donnée sensible. Rien n'y
/// serait de toute façon protégé — Discord montre le <c>custom_id</c> à quiconque inspecte le
/// message.
/// </para>
/// <para>
/// Discord limite un <c>custom_id</c> à 100 caractères. Les capacités confirmables aujourd'hui
/// tiennent large — un chemin d'accès pris comme identifiant de téléchargement fait au plus une
/// quarantaine de caractères — mais l'encodage lève plutôt que de tronquer silencieusement, pour
/// qu'un dépassement futur se voie au démarrage et non au premier clic raté.
/// </para>
/// </remarks>
internal static class DiscordConfirmationToken
{
    private const string ConfirmPrefix = "confirm:";
    private const int MaxLength = 100;

    /// <summary><c>custom_id</c> du bouton d'annulation. Statique : annuler n'a besoin de rejouer aucun appel.</summary>
    public const string Cancel = "cancel";

    public static string Encode(string capabilityKey, IReadOnlyDictionary<string, object?> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityKey);
        ArgumentNullException.ThrowIfNull(arguments);

        var query = string.Join('&', arguments
            .Where(pair => pair.Value is not null)
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}=" +
                            Uri.EscapeDataString(Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? "")));

        var id = query.Length == 0
            ? $"{ConfirmPrefix}{capabilityKey}"
            : $"{ConfirmPrefix}{capabilityKey}?{query}";

        if (id.Length > MaxLength)
        {
            throw new InvalidOperationException(
                $"« {capabilityKey} » : custom_id de {id.Length} caractères, maximum {MaxLength} — " +
                "un argument confirmable est devenu trop long pour tenir dans un bouton Discord.");
        }

        return id;
    }

    /// <summary><c>null</c> si <paramref name="customId"/> ne porte pas une confirmation en attente.</summary>
    public static (string CapabilityKey, IReadOnlyDictionary<string, object?> Arguments)? TryDecode(string customId)
    {
        if (string.IsNullOrEmpty(customId) || !customId.StartsWith(ConfirmPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = customId[ConfirmPrefix.Length..];
        var separator = rest.IndexOf('?');
        var capabilityKey = separator < 0 ? rest : rest[..separator];
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (separator >= 0)
        {
            foreach (var pair in rest[(separator + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2)
                {
                    arguments[Uri.UnescapeDataString(kv[0])] = Uri.UnescapeDataString(kv[1]);
                }
            }
        }

        return (capabilityKey, arguments);
    }
}
