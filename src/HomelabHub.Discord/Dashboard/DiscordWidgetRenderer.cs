using System.Globalization;
using System.Text;
using System.Text.Json;
using HomelabHub.Abstractions.Dashboard;
using HomelabHub.Modules.Media.Correlation;

namespace HomelabHub.Discord.Dashboard;

/// <summary>
/// Rend un bloc de tableau de bord en texte Discord.
/// </summary>
/// <remarks>
/// <para>
/// <b>Duplication assumée</b> (ADR-0006) : chaque adaptateur porte son propre rendu, il n'y a
/// pas de modèle de présentation partagé entre Discord et le web. <see cref="MediaOverview"/>
/// obtient un rendu dédié — le palmarès à cinq et le résumé chiffré demandés au cadrage ne se
/// lisent pas correctement en clé/valeur brut. Tout widget futur sans rendu dédié retombe sur ce
/// repli générique plutôt que de disparaître du tableau de bord.
/// </para>
/// </remarks>
internal static class DiscordWidgetRenderer
{
    public static string Render(WidgetPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return payload.Data switch
        {
            MediaOverview overview => RenderMediaOverview(overview),
            _ => RenderGeneric(payload),
        };
    }

    private static string RenderMediaOverview(MediaOverview overview)
    {
        var lines = new List<string> { "**📥 Téléchargements**" };

        if (overview.ObservedAt is null)
        {
            lines.Add("_Aucune observation depuis le démarrage._");
            return string.Join('\n', lines);
        }

        if (overview.Top.Count == 0)
        {
            lines.Add("_Rien à signaler._");
        }

        foreach (var journey in overview.Top)
        {
            lines.Add(RenderJourney(journey));
        }

        if (overview.UnavailableSources.Count > 0)
        {
            lines.Add($"⚠️ Injoignable(s) : {string.Join(", ", overview.UnavailableSources)}");
        }

        // string.Create(CultureInfo, $"...") n'accepte qu'un littéral interpolé isolé : dès
        // qu'il est composé par concaténation ou ternaire, comme ici, le compilateur ne peut
        // plus le convertir en gestionnaire de chaîne interpolée. D'où .ToString(Invariant) sur
        // chaque valeur plutôt que le raccourci habituel.
        var speedSuffix = overview.DownloadSpeed > 0
            ? " · " + FormatBytes(overview.DownloadSpeed) + "/s"
            : "";

        lines.Add("_" + overview.Downloading.ToString(CultureInfo.InvariantCulture) + " en cours · " +
                   overview.Importing.ToString(CultureInfo.InvariantCulture) + " en import · " +
                   overview.NeedsAttention.ToString(CultureInfo.InvariantCulture) + " à surveiller · " +
                   FormatBytes(overview.BytesRemaining) + " restants" + speedSuffix + "_");

        return string.Join('\n', lines);
    }

    private static string RenderJourney(JourneySummary journey)
    {
        // L'icône porte l'essentiel d'un coup d'œil : c'est ce qui compte sur un message
        // permanent lu depuis un téléphone, pas depuis une page qu'on peut inspecter en détail.
        var icon = journey.NeedsAttention ? "🔴"
            : journey.State == JourneyState.Importing ? "📦"
            : "⬇️";

        var title = journey.Title ?? "(titre inconnu)";
        var percent = (int)Math.Round(journey.Progress * 100, MidpointRounding.AwayFromZero);

        var speedSuffix = journey.DownloadSpeed > 0 ? " · " + FormatBytes(journey.DownloadSpeed) + "/s" : "";
        var etaSuffix = journey.EstimatedTimeLeft is { } eta ? " · reste " + FormatDuration(eta) : "";

        var detail = journey.NeedsAttention
            ? "bloqué"
            : percent.ToString(CultureInfo.InvariantCulture) + "%" + speedSuffix + etaSuffix;

        return $"{icon} {title} — {detail}";
    }

    /// <summary>
    /// Un widget sans rendu dédié reste lisible plutôt que de disparaître : chaque propriété
    /// scalaire de la donnée devient une ligne. Un objet ou une liste imbriquée est résumé par
    /// son nombre d'éléments plutôt que déplié — le repli n'a pas vocation à tout montrer, ADR-0006.
    /// </summary>
    private static string RenderGeneric(WidgetPayload payload)
    {
        var builder = new StringBuilder().Append("**").Append(payload.WidgetKey).Append("**\n");

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload.Data));

        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in document.RootElement.EnumerateObject())
            {
                builder.Append("- ").Append(property.Name).Append(" : ").Append(Describe(property.Value)).Append('\n');
            }
        }
        else
        {
            builder.Append(Describe(document.RootElement));
        }

        return builder.ToString().TrimEnd('\n');
    }

    private static string Describe(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => $"{element.GetArrayLength()} élément(s)",
        JsonValueKind.Object => "(détail)",
        JsonValueKind.String => element.GetString() ?? "",
        _ => element.ToString(),
    };

    private static string FormatBytes(double bytes)
    {
        string[] units = ["o", "Ko", "Mo", "Go", "To"];
        var value = Math.Abs(bytes);
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{value:0.#} {units[unit]}");
    }

    private static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1
        ? string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalHours} h {duration.Minutes:00}")
        : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, duration.Minutes)} min");
}
