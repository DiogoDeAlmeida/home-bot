using System.Globalization;
using HomelabHub.Core.Anomalies;

namespace HomelabHub.Host.Api;

internal static class AnomalyEndpoints
{
    public static void MapAnomalies(this IEndpointRouteBuilder app)
    {
        var anomalies = app.MapGroup("/api/anomalies").RequireAuthorization();

        anomalies.MapGet("/", (IAnomalyEngine engine, bool? all) =>
            Results.Ok((all == true ? engine.All : engine.Active).Select(a => new
            {
                a.DedupeKey,
                a.ModuleKey,
                a.Type,
                severity = (int)a.Severity,
                a.Title,
                a.Body,
                state = a.State.ToString(),
                a.OpenedAt,
                a.LastSeenAt,
                a.ResolvedAt,
                a.SnoozedUntil,
                a.Occurrences,
                durationSeconds = (long)a.Duration.TotalSeconds,
                a.Data,
            })));

        anomalies.MapPost("/{key}/snooze", (string key, SnoozeRequest request, IAnomalyEngine engine) =>
        {
            // Deux formes, toutes deux prévues au cadrage : une échéance, ou « jusqu'à
            // résolution » — qui ne se réarme qu'après un passage effectif par l'état résolu.
            var until = request.Hours is { } hours and > 0
                ? DateTimeOffset.UtcNow.AddHours(hours)
                : (DateTimeOffset?)null;

            return engine.Snooze(Uri.UnescapeDataString(key), until, DateTimeOffset.UtcNow)
                ? Results.Ok(new
                {
                    snoozed = true,
                    until = until?.ToString("O", CultureInfo.InvariantCulture),
                })
                : Results.NotFound(new { error = "unknown_or_resolved" });
        });
    }

    /// <param name="Hours">Durée du sommeil. Absente ou nulle signifie « jusqu'à résolution ».</param>
    internal sealed record SnoozeRequest(int? Hours);
}
