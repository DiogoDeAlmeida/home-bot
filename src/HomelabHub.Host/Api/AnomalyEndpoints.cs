using HomelabHub.Core.Anomalies;

namespace HomelabHub.Host.Api;

/// <remarks>
/// La mise en sommeil n'a plus d'endpoint dédié : c'est désormais la capacité
/// <c>hub.anomaly.snooze</c>, exécutée comme toute autre mutation via
/// <c>POST /api/capabilities/hub.anomaly.snooze</c>. Une seconde voie d'écriture ici aurait
/// contourné l'autorisation, la confirmation et le journal d'audit que ce chemin porte déjà.
/// Seule la lecture reste propre à ce groupe.
/// </remarks>
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
    }
}
