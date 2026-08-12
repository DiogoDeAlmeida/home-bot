using System.Globalization;
using HomelabHub.Core.Anomalies;

namespace HomelabHub.Discord.Notifications;

/// <summary>
/// Rend une anomalie en message Discord — un message par anomalie, édité en place à chaque
/// transition plutôt que reposté (ADR-0005 : ce sont les transitions qui notifient, pas les
/// republications).
/// </summary>
internal static class DiscordAnomalyRenderer
{
    public static string Render(Anomaly anomaly)
    {
        ArgumentNullException.ThrowIfNull(anomaly);

        var icon = anomaly.State switch
        {
            AnomalyState.Resolved => "✅",
            AnomalyState.Snoozed => "😴",
            _ => anomaly.Severity == Abstractions.Events.HubEventSeverity.Critical ? "🔴" : "⚠️",
        };

        var lines = new List<string> { $"{icon} **{anomaly.Title}**" };

        if (!string.IsNullOrWhiteSpace(anomaly.Body))
        {
            lines.Add(anomaly.Body);
        }

        lines.Add(anomaly.State switch
        {
            AnomalyState.Resolved => "_Résolue._",
            // Une heure absolue, pas une durée relative : LastSeenAt n'avance pas pendant le
            // sommeil, un calcul « jusqu'à » à partir de lui serait faux dès la seconde suivante.
            AnomalyState.Snoozed when anomaly.SnoozedUntil is { } until =>
                $"_En sommeil jusqu'à {until:HH:mm} UTC._",
            AnomalyState.Snoozed => "_En sommeil jusqu'à sa résolution._",
            _ => $"_Ouverte depuis {FormatDuration(anomaly.Duration)}._",
        });

        return string.Join('\n', lines);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
        {
            return duration.Days.ToString(CultureInfo.InvariantCulture) + " j";
        }

        if (duration.TotalHours >= 1)
        {
            return (int)duration.TotalHours + " h";
        }

        return Math.Max(1, duration.Minutes).ToString(CultureInfo.InvariantCulture) + " min";
    }
}
