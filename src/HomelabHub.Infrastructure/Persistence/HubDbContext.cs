using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HomelabHub.Infrastructure.Persistence;

/// <summary>
/// Ce que le hub possède réellement.
/// </summary>
/// <remarks>
/// <para>
/// La base n'existait pas jusqu'ici, et ce report était délibéré (ADR-0007) : la configuration
/// est un dictionnaire sans relation, et les parcours média sont un état <b>dérivé</b> qui se
/// reconstruit à chaque cycle (ADR-0015). Rien de tout cela ne justifiait EF Core.
/// </para>
/// <para>
/// La table d'anomalies a changé cela. « Depuis quand cette anomalie dure » ne se reconstruit
/// pas : aucun service ne le sait. C'est le premier état réellement possédé par le hub, et
/// c'est ce qui a déclenché la base — pas une envie d'architecture.
/// </para>
/// <para>
/// <b>Ne pas y mettre ce qui est dérivable.</b> Persister un snapshot média ferait diverger le
/// hub de ses sources, et rendrait ADR-0015 faux en pratique tout en le laissant vrai sur le
/// papier.
/// </para>
/// </remarks>
public sealed class HubDbContext(DbContextOptions<HubDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Les instants sont stockés en ticks UTC, pas en texte.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Ce n'est pas un détail de sérialisation.</b> SQLite n'a pas de type date : le provider
    /// écrirait un <c>DateTimeOffset</c> sous forme de texte avec son décalage — et refuse alors
    /// de traduire la moindre comparaison, parce que l'ordre lexicographique de deux instants
    /// notés dans des fuseaux différents ne correspond pas à leur ordre chronologique. Toute la
    /// rétention repose sur des comparaisons de dates : sans cette conversion, elle ne
    /// s'exécuterait tout simplement pas.
    /// </para>
    /// <para>
    /// Un entier de ticks UTC est comparable, indexable, et exact à la centaine de nanosecondes.
    /// Le décalage d'origine est <b>perdu</b>, volontairement : le hub raisonne en UTC de bout en
    /// bout et n'affiche l'heure de Paris qu'au dernier moment. Conserver un décalage qu'aucune
    /// lecture n'utilise ne rachèterait pas des comparaisons qui ne fonctionnent pas.
    /// </para>
    /// </remarks>
    private static readonly ValueConverter<DateTimeOffset, long> Instant =
        new(value => value.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

    private static readonly ValueConverter<DateTimeOffset?, long?> NullableInstant =
        new(value => value!.Value.UtcTicks, ticks => new DateTimeOffset(ticks!.Value, TimeSpan.Zero));

    public DbSet<AnomalyEntity> Anomalies => Set<AnomalyEntity>();

    public DbSet<JournalEntity> Journal => Set<JournalEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<AnomalyEntity>(entity =>
        {
            // La clé de déduplication est l'identité de l'anomalie dans la durée : c'est
            // naturellement la clé primaire, et cela rend le doublon impossible par construction.
            entity.HasKey(a => a.DedupeKey);
            entity.Property(a => a.DedupeKey).HasMaxLength(200);
            entity.Property(a => a.ModuleKey).HasMaxLength(20).IsRequired();
            entity.Property(a => a.Type).HasMaxLength(100).IsRequired();
            entity.Property(a => a.Title).HasMaxLength(500).IsRequired();

            entity.Property(a => a.OpenedAt).HasConversion(Instant);
            entity.Property(a => a.LastSeenAt).HasConversion(Instant);
            entity.Property(a => a.ResolvedAt).HasConversion(NullableInstant);
            entity.Property(a => a.SnoozedUntil).HasConversion(NullableInstant);

            // Un état ouvert se lit à chaque cycle et à chaque affichage.
            entity.HasIndex(a => new { a.ModuleKey, a.State });
            entity.HasIndex(a => a.ResolvedAt);
        });

        modelBuilder.Entity<JournalEntity>(entity =>
        {
            entity.HasKey(j => j.Id);
            entity.Property(j => j.ModuleKey).HasMaxLength(20).IsRequired();
            entity.Property(j => j.Type).HasMaxLength(100).IsRequired();
            entity.Property(j => j.Title).HasMaxLength(500).IsRequired();
            entity.Property(j => j.OccurredAt).HasConversion(Instant);

            // La purge balaie cette colonne à chaque passage quotidien.
            entity.HasIndex(j => j.OccurredAt);
        });
    }
}

/// <summary>Une anomalie, telle qu'elle survit à un redémarrage.</summary>
public sealed class AnomalyEntity
{
    public required string DedupeKey { get; set; }

    public required string ModuleKey { get; set; }

    public required string Type { get; set; }

    public int Severity { get; set; }

    public required string Title { get; set; }

    public string? Body { get; set; }

    /// <summary>Données structurées, sérialisées. Un dictionnaire libre ne mérite pas sa table.</summary>
    public string? DataJson { get; set; }

    public int State { get; set; }

    public DateTimeOffset OpenedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public DateTimeOffset? SnoozedUntil { get; set; }

    public int Occurrences { get; set; }
}

/// <summary>Un événement du journal.</summary>
/// <remarks>
/// Contrairement aux anomalies, le journal est un flux : il porte chaque republication, et c'est
/// voulu. Sa rétention est donc bornée en âge et en nombre de lignes, sans quoi il grossirait
/// sans fin sur un LXC.
/// </remarks>
public sealed class JournalEntity
{
    public long Id { get; set; }

    public required string ModuleKey { get; set; }

    public required string Type { get; set; }

    public int Severity { get; set; }

    public required string Title { get; set; }

    public string? Body { get; set; }

    public string? DedupeKey { get; set; }

    public string? DataJson { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
