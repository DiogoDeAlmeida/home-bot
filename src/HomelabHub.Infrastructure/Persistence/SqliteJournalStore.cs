using System.Text.Json;
using HomelabHub.Abstractions.Events;
using HomelabHub.Core.Events;
using Microsoft.EntityFrameworkCore;

namespace HomelabHub.Infrastructure.Persistence;

/// <summary>
/// Le journal sur disque, avec sa double rétention.
/// </summary>
/// <remarks>
/// <para>
/// <b>Écriture synchrone, assumée.</b> Un tampon vidé en arrière-plan aurait évité une
/// transaction par événement, au prix d'une fenêtre où l'interface n'affiche pas encore ce que
/// le hub vient de journaliser — exactement le moment où on regarde le journal. Le volume réel
/// est de quelques dizaines de lignes par cycle de trente secondes ; en mode WAL, une insertion
/// SQLite locale se compte en dizaines de microsecondes. Le tampon serait une optimisation
/// contre un problème qui n'existe pas.
/// </para>
/// </remarks>
internal sealed class SqliteJournalStore(IDbContextFactory<HubDbContext> contexts) : IJournalStore
{
    public void Append(HubEvent hubEvent)
    {
        ArgumentNullException.ThrowIfNull(hubEvent);

        using var context = contexts.CreateDbContext();

        context.Journal.Add(new JournalEntity
        {
            ModuleKey = hubEvent.ModuleKey,
            Type = hubEvent.Type,
            Severity = (int)hubEvent.Severity,
            Title = hubEvent.Title,
            Body = hubEvent.Body,
            DedupeKey = hubEvent.DedupeKey,
            DataJson = hubEvent.Data is { Count: > 0 } data ? JsonSerializer.Serialize(data) : null,
            OccurredAt = hubEvent.OccurredAt,
        });

        context.SaveChanges();
    }

    public IReadOnlyList<HubEvent> Recent(int count, HubEventSeverity? minimumSeverity)
    {
        using var context = contexts.CreateDbContext();

        var query = context.Journal.AsNoTracking().AsQueryable();

        if (minimumSeverity is { } minimum)
        {
            query = query.Where(j => j.Severity >= (int)minimum);
        }

        // Le tri se fait sur l'identifiant, pas sur la date : deux événements du même cycle
        // portent souvent le même horodatage à la milliseconde près, et l'ordre d'insertion est
        // le seul qui soit stable.
        return [.. query
            .OrderByDescending(j => j.Id)
            .Take(Math.Clamp(count, 1, 1_000))
            .AsEnumerable()
            .Select(Map)];
    }

    public int Purge(DateTimeOffset cutoff, int maximumRows)
    {
        using var context = contexts.CreateDbContext();

        var removed = context.Journal.Where(j => j.OccurredAt < cutoff).ExecuteDelete();

        // Seconde borne. Les identifiants sont croissants, donc « garder les N derniers » se dit
        // « supprimer tout ce qui est sous le N-ième identifiant en partant de la fin » — une
        // seule comparaison, sans compter ni charger quoi que ce soit.
        var floor = context.Journal
            .OrderByDescending(j => j.Id)
            .Skip(maximumRows)
            .Select(j => (long?)j.Id)
            .FirstOrDefault();

        if (floor is { } threshold)
        {
            removed += context.Journal.Where(j => j.Id <= threshold).ExecuteDelete();
        }

        return removed;
    }

    private static HubEvent Map(JournalEntity entity) => new(
        ModuleKey: entity.ModuleKey,
        Type: entity.Type,
        Severity: (HubEventSeverity)entity.Severity,
        Title: entity.Title,
        Body: entity.Body,
        DedupeKey: entity.DedupeKey,
        Data: JsonColumn.Read(entity.DataJson),
        OccurredAt: entity.OccurredAt);
}
