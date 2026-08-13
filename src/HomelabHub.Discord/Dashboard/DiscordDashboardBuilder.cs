using System.Globalization;
using System.Text.Json;
using global::Discord;
using HomelabHub.Abstractions.Dashboard;
using HomelabHub.Modules.Media.Correlation;

namespace HomelabHub.Discord.Dashboard;

/// <summary>
/// Prototype de rendu du tableau de bord en Components V2, à comparer avec
/// <see cref="DiscordWidgetRenderer"/> (texte brut) avant de généraliser à autre chose.
/// </summary>
/// <remarks>
/// <para>
/// <b>Volontairement séparé, pas une réécriture sur place.</b> Le rendu visuel se discute à
/// l'œil, sur une vraie capture, pas en relisant du code — trois ou quatre itérations sont
/// attendues avant d'y toucher pour de bon. Tant que ce fichier existe à côté de l'ancien, revenir
/// en arrière ne coûte qu'un branchement dans <c>DiscordGatewayService</c>, jamais une
/// réécriture. Les réponses de commande et les notifications d'anomalie restent sur l'ancien
/// rendu jusqu'à ce que le style du tableau de bord soit arrêté.
/// </para>
/// <para>
/// <b>Composants V2, pas des embeds classiques.</b> Trois raisons, dans l'ordre où elles ont
/// pesé : la couleur de bordure d'un <c>ContainerBuilder</c> (<c>WithAccentColor</c>) peut
/// refléter un état — sain ou à surveiller — ce qu'un embed fait aussi mais moins finement une
/// fois qu'il faut plusieurs blocs dans un seul message ; les boutons déjà écrits
/// (confirmation, sommeil) restent utilisables tels quels via <c>ContainerBuilder.WithActionRow</c>,
/// aucune réécriture de <c>DiscordConfirmationToken</c> ni <c>DiscordSnoozeButtons</c> ; et c'est
/// la direction que Discord pousse pour ses propres surfaces, stable depuis Discord.Net 3.18 (ce
/// dépôt est sur 3.20.1). Le coût : un message en V2 ne peut plus avoir de <c>Content</c> ni
/// d'embed classique — c'est l'un ou l'autre, jamais les deux à la fois.
/// </para>
/// </remarks>
internal static class DiscordDashboardBuilder
{
    /// <summary>Bleu Discord neutre — rien à signaler.</summary>
    private static readonly Color HealthyAccent = new(0x5865F2);

    /// <summary>Au moins un parcours demande une intervention.</summary>
    private static readonly Color AttentionAccent = new(0xE67E22);

    public static MessageComponent Build(IReadOnlyList<WidgetPayload> widgets)
    {
        ArgumentNullException.ThrowIfNull(widgets);

        var root = new ComponentBuilderV2();

        if (widgets.Count == 0)
        {
            root.WithContainer(new ContainerBuilder().WithTextDisplay("_Aucun widget actif._"));
            return root.Build();
        }

        foreach (var widget in widgets)
        {
            root.WithContainer(BuildWidgetContainer(widget));
        }

        root.WithTextDisplay(string.Create(CultureInfo.InvariantCulture,
            $"-# Mis à jour à {DateTimeOffset.UtcNow:HH:mm} UTC"));

        return root.Build();
    }

    private static ContainerBuilder BuildWidgetContainer(WidgetPayload payload) => payload.Data switch
    {
        MediaOverview overview => BuildMediaOverviewContainer(overview),
        _ => BuildGenericContainer(payload.Data, payload.WidgetKey),
    };

    private static ContainerBuilder BuildMediaOverviewContainer(MediaOverview overview)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(overview.NeedsAttention > 0 ? AttentionAccent : HealthyAccent)
            .WithTextDisplay("## 📥 Téléchargements");

        if (overview.ObservedAt is null)
        {
            return container.WithTextDisplay("_Aucune observation depuis le démarrage._");
        }

        if (overview.Top.Count == 0)
        {
            return container.WithTextDisplay("_Rien à signaler._");
        }

        container.WithSeparator(spacing: SeparatorSpacingSize.Small);

        foreach (var journey in overview.Top)
        {
            container.WithTextDisplay(RenderJourneyLine(journey));
        }

        if (overview.UnavailableSources.Count > 0)
        {
            container.WithTextDisplay(
                $"⚠️ Injoignable(s) : {string.Join(", ", overview.UnavailableSources)}");
        }

        container.WithSeparator(spacing: SeparatorSpacingSize.Small);
        container.WithTextDisplay(RenderSummaryLine(overview));

        return container;
    }

    /// <summary>
    /// Une ligne par parcours : icône d'état, titre, barre de progression, détail. La barre
    /// donne d'un coup d'œil ce que le pourcentage textuel demandait de lire — l'intérêt même
    /// de ce prototype par rapport à l'ancien rendu.
    /// </summary>
    private static string RenderJourneyLine(JourneySummary journey)
    {
        var icon = journey.NeedsAttention ? "🔴"
            : journey.State switch
            {
                JourneyState.Importing => "📦",
                JourneyState.Available => "✅",
                _ => "⬇️",
            };

        var title = journey.Title ?? "(titre inconnu)";
        var percent = (int)Math.Round(journey.Progress * 100, MidpointRounding.AwayFromZero);
        var bar = ProgressBar(journey.Progress);

        var speedSuffix = journey.DownloadSpeed > 0 ? " · " + FormatBytes(journey.DownloadSpeed) + "/s" : "";
        var etaSuffix = journey.EstimatedTimeLeft is { } eta ? " · reste " + FormatDuration(eta) : "";

        var detail = journey.NeedsAttention
            ? "bloqué"
            : percent.ToString(CultureInfo.InvariantCulture) + "%" + speedSuffix + etaSuffix;

        // Toujours en code inline : /media pause et /media resume attendent exactement cette
        // valeur en argument (voir DiscordWidgetRenderer.RenderJourney, même contrainte).
        var ids = string.Concat(journey.DownloadIds.Select(id => $"\n   `{id}`"));

        return $"{icon} **{title}**\n`{bar}` {detail}{ids}";
    }

    private static string RenderSummaryLine(MediaOverview overview)
    {
        var speedSuffix = overview.DownloadSpeed > 0 ? " · " + FormatBytes(overview.DownloadSpeed) + "/s" : "";

        return "-# " + overview.Downloading.ToString(CultureInfo.InvariantCulture) + " en cours · " +
               overview.Importing.ToString(CultureInfo.InvariantCulture) + " en import · " +
               overview.NeedsAttention.ToString(CultureInfo.InvariantCulture) + " à surveiller · " +
               FormatBytes(overview.BytesRemaining) + " restants" + speedSuffix;
    }

    /// <summary>
    /// Douze segments : assez pour distinguer 8 % de 15 % d'un coup d'œil, sans dépasser la
    /// largeur d'un écran de téléphone une fois le titre et le détail ajoutés à côté.
    /// </summary>
    private static string ProgressBar(double progress, int segments = 12)
    {
        var filled = (int)Math.Round(Math.Clamp(progress, 0, 1) * segments, MidpointRounding.AwayFromZero);
        return new string('█', filled) + new string('░', segments - filled);
    }

    /// <summary>
    /// Même repli générique clé/valeur que l'ancien rendu (ADR-0006) — un widget futur sans
    /// rendu dédié reste lisible plutôt que de disparaître du tableau de bord.
    /// </summary>
    private static ContainerBuilder BuildGenericContainer(object? data, string title)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(HealthyAccent)
            .WithTextDisplay($"## {title}");

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(data));

        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in document.RootElement.EnumerateObject())
            {
                container.WithTextDisplay($"**{property.Name}** : {Describe(property.Value)}");
            }
        }
        else
        {
            container.WithTextDisplay(Describe(document.RootElement));
        }

        return container;
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
