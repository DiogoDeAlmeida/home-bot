namespace HomelabHub.Discord.Notifications;

/// <summary>
/// <c>custom_id</c> des deux boutons de sommeil sur un message d'anomalie.
/// </summary>
/// <remarks>
/// <para>
/// Séparé de <see cref="Interactions.DiscordConfirmationToken"/> plutôt que généralisé avec
/// lui : <c>hub.anomaly.snooze</c> ne demande pas de confirmation (ADR — voir la remarque sur
/// <c>AnomalySnoozeCapability</c>), ces boutons exécutent donc directement, sans étape
/// intermédiaire. Mélanger les deux mécanismes dans un seul type aurait fait porter à l'un la
/// complexité de l'autre sans qu'aucun cas réel ne le demande.
/// </para>
/// <para>
/// Sans état côté serveur, comme la confirmation : la clé de déduplication et la durée
/// choisie voyagent entières dans le <c>custom_id</c>.
/// </para>
/// </remarks>
internal static class DiscordSnoozeButtons
{
    private const string Prefix = "snooze:";
    private const int MaxLength = 100;

    public static string Encode(string dedupeKey, int? hours)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dedupeKey);

        var id = hours is { } h
            ? $"{Prefix}{Uri.EscapeDataString(dedupeKey)}?hours={h}"
            : $"{Prefix}{Uri.EscapeDataString(dedupeKey)}";

        if (id.Length > MaxLength)
        {
            throw new InvalidOperationException(
                $"« {dedupeKey} » : custom_id de sommeil de {id.Length} caractères, maximum {MaxLength}.");
        }

        return id;
    }

    public static (string DedupeKey, int? Hours)? TryDecode(string customId)
    {
        if (string.IsNullOrEmpty(customId) || !customId.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = customId[Prefix.Length..];
        var separator = rest.IndexOf('?');
        var dedupeKey = Uri.UnescapeDataString(separator < 0 ? rest : rest[..separator]);

        int? hours = null;
        if (separator >= 0)
        {
            var equals = rest.IndexOf('=', separator);
            if (equals >= 0 && int.TryParse(rest[(equals + 1)..], out var parsed))
            {
                hours = parsed;
            }
        }

        return (dedupeKey, hours);
    }
}
