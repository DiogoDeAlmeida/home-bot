using System.Text.Json;
using HomelabHub.Abstractions.Events;
using HomelabHub.Core.Anomalies;
using Microsoft.EntityFrameworkCore;

namespace HomelabHub.Infrastructure.Persistence;

/// <summary>La table d'anomalies sur disque.</summary>
/// <remarks>
/// Un contexte par opération, créé à la demande : <c>DbContext</c> n'est pas sûr entre threads,
/// et le moteur d'anomalies écrit depuis autant de boucles qu'il y a de pollers.
/// </remarks>
internal sealed class SqliteAnomalyStore(IDbContextFactory<HubDbContext> contexts) : IAnomalyStore
{
    public IReadOnlyList<Anomaly> Load()
    {
        using var context = contexts.CreateDbContext();

        // AsEnumerable avant Map : la projection est du code C#, pas du SQL.
        return [.. context.Anomalies.AsNoTracking().AsEnumerable().Select(Map)];
    }

    public void Save(IReadOnlyList<Anomaly> anomalies)
    {
        ArgumentNullException.ThrowIfNull(anomalies);

        if (anomalies.Count == 0)
        {
            return;
        }

        using var context = contexts.CreateDbContext();

        var keys = anomalies.Select(a => a.DedupeKey).ToList();
        var existing = context.Anomalies
            .Where(a => keys.Contains(a.DedupeKey))
            .ToDictionary(a => a.DedupeKey, StringComparer.Ordinal);

        foreach (var anomaly in anomalies)
        {
            if (existing.TryGetValue(anomaly.DedupeKey, out var entity))
            {
                Apply(anomaly, entity);
            }
            else
            {
                var created = new AnomalyEntity
                {
                    DedupeKey = anomaly.DedupeKey,
                    ModuleKey = anomaly.ModuleKey,
                    Type = anomaly.Type,
                    Title = anomaly.Title,
                };

                Apply(anomaly, created);
                context.Anomalies.Add(created);
            }
        }

        context.SaveChanges();
    }

    public int PurgeResolvedBefore(DateTimeOffset cutoff)
    {
        using var context = contexts.CreateDbContext();

        // Une anomalie ouverte n'est jamais purgée, quel que soit son âge : « bloquée depuis
        // trois semaines » est exactement ce qu'il faut garder.
        return context.Anomalies
            .Where(a => a.State == (int)AnomalyState.Resolved
                        && a.ResolvedAt != null && a.ResolvedAt < cutoff)
            .ExecuteDelete();
    }

    private static void Apply(Anomaly source, AnomalyEntity target)
    {
        target.ModuleKey = source.ModuleKey;
        target.Type = source.Type;
        target.Severity = (int)source.Severity;
        target.Title = source.Title;
        target.Body = source.Body;
        target.DataJson = source.Data is { Count: > 0 } data ? JsonSerializer.Serialize(data) : null;
        target.State = (int)source.State;
        target.OpenedAt = source.OpenedAt;
        target.LastSeenAt = source.LastSeenAt;
        target.ResolvedAt = source.ResolvedAt;
        target.SnoozedUntil = source.SnoozedUntil;
        target.Occurrences = source.Occurrences;
    }

    private static Anomaly Map(AnomalyEntity entity) => new(
        DedupeKey: entity.DedupeKey,
        ModuleKey: entity.ModuleKey,
        Type: entity.Type,
        Severity: (HubEventSeverity)entity.Severity,
        Title: entity.Title,
        Body: entity.Body,
        Data: JsonColumn.Read(entity.DataJson),
        State: (AnomalyState)entity.State,
        OpenedAt: entity.OpenedAt,
        LastSeenAt: entity.LastSeenAt,
        ResolvedAt: entity.ResolvedAt,
        SnoozedUntil: entity.SnoozedUntil,
        Occurrences: entity.Occurrences);
}

/// <summary>Lecture tolérante d'une colonne JSON.</summary>
internal static class JsonColumn
{
    /// <remarks>
    /// Un JSON illisible en base ne doit pas empêcher le hub de démarrer : la donnée
    /// structurée d'une anomalie sert à l'affichage, pas à sa machine à états. On perd un
    /// détail, on ne perd pas la table.
    /// </remarks>
    public static IReadOnlyDictionary<string, string>? Read(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
