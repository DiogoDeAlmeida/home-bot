using HomelabHub.Core.Anomalies;
using HomelabHub.Core.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HomelabHub.Core.Tests;

/// <summary>
/// La purge de rétention — jamais exercée en conditions réelles avant cette tranche, seulement
/// via ses deux méthodes de bas niveau (<c>AnomalyEngine.PurgeResolved</c>,
/// <c>SqliteJournalStore.Purge</c>). Ces tests couvrent l'orchestration qui les relie : le calcul
/// de la fenêtre d'âge, la lecture des seuils configurés, la tolérance aux pannes.
/// </summary>
public sealed class RetentionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    private static RetentionService NewService(IJournalStore? journal = null,
                                                RecordingConfigStore? config = null,
                                                AnomalyEngine? anomalies = null) =>
        new(journal ?? new RecordingJournalStore(),
            anomalies ?? new AnomalyEngine(new RecordingAnomalyStore(), NullLogger<AnomalyEngine>.Instance),
            config ?? new RecordingConfigStore(),
            new FixedTime(Now),
            NullLogger<RetentionService>.Instance);

    [Fact]
    public void Purge_calcule_la_fenetre_dage_depuis_la_retention_configuree()
    {
        var config = new RecordingConfigStore();
        config.Set(Configuration.HubSettings.JournalRetentionDaysKey, "7");
        var journal = new RecordingJournalStore();

        NewService(journal, config).Purge();

        Assert.Equal(Now.AddDays(-7), journal.LastPurgeArgs!.Value.Cutoff);
    }

    [Fact]
    public void Sans_reglage_la_retention_par_defaut_est_de_14_jours()
    {
        var journal = new RecordingJournalStore();

        NewService(journal).Purge();

        Assert.Equal(Now.AddDays(-14), journal.LastPurgeArgs!.Value.Cutoff);
    }

    [Fact]
    public void La_borne_de_lignes_configuree_est_transmise_au_magasin()
    {
        var config = new RecordingConfigStore();
        config.Set(Configuration.HubSettings.JournalMaximumRowsKey, "5000");
        var journal = new RecordingJournalStore();

        NewService(journal, config).Purge();

        Assert.Equal(5000, journal.LastPurgeArgs!.Value.MaximumRows);
    }

    [Fact]
    public void Une_borne_de_lignes_trop_basse_retombe_sur_mille_lignes_minimum()
    {
        // Un réglage à 10 ne doit pas purger l'essentiel de l'historique existant.
        var config = new RecordingConfigStore();
        config.Set(Configuration.HubSettings.JournalMaximumRowsKey, "10");
        var journal = new RecordingJournalStore();

        NewService(journal, config).Purge();

        Assert.Equal(1_000, journal.LastPurgeArgs!.Value.MaximumRows);
    }

    [Fact]
    public void Une_retention_a_zero_ou_negative_retombe_sur_un_jour_minimum()
    {
        // Un réglage à 0 ne doit pas se traduire par une purge de tout l'historique existant.
        var config = new RecordingConfigStore();
        config.Set(Configuration.HubSettings.JournalRetentionDaysKey, "0");
        var journal = new RecordingJournalStore();

        NewService(journal, config).Purge();

        Assert.Equal(Now.AddDays(-1), journal.LastPurgeArgs!.Value.Cutoff);
    }

    [Fact]
    public void Purge_retourne_ce_que_les_deux_magasins_ont_supprime()
    {
        var journal = new RecordingJournalStore();
        journal.SetNextPurgeResult(12);

        var (events, resolved) = NewService(journal).Purge();

        Assert.Equal(12, events);
        Assert.Equal(0, resolved); // aucune anomalie résolue dans un moteur tout neuf
    }

    [Fact]
    public void Une_panne_du_magasin_ne_remonte_pas_a_lappelant()
    {
        // Convention §14 : une purge ratée dégrade, elle ne fait pas tomber le hub. Le journal
        // applicatif suffit à le savoir — pas une exception qui remonterait jusqu'à la capacité
        // ou la boucle d'arrière-plan.
        var result = NewService(new ThrowingJournalStore()).Purge();

        Assert.Equal((0, 0), result);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ThrowingJournalStore : IJournalStore
    {
        public void Append(Abstractions.Events.HubEvent hubEvent) { }

        public IReadOnlyList<Abstractions.Events.HubEvent> Recent(
            int count, Abstractions.Events.HubEventSeverity? minimumSeverity) => [];

        public int Purge(DateTimeOffset cutoff, int maximumRows) =>
            throw new InvalidOperationException("disque plein");
    }
}
