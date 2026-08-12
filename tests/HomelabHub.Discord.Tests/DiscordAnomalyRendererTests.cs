using HomelabHub.Abstractions.Events;
using HomelabHub.Core.Anomalies;
using HomelabHub.Discord.Notifications;
using Xunit;

namespace HomelabHub.Discord.Tests;

/// <summary>Le rendu d'une anomalie en message Discord, un par état.</summary>
public sealed class DiscordAnomalyRendererTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);

    private static Anomaly Anomaly(AnomalyState state = AnomalyState.Open,
                                   HubEventSeverity severity = HubEventSeverity.Warning,
                                   DateTimeOffset? snoozedUntil = null,
                                   TimeSpan? duration = null) =>
        new(DedupeKey: "media.import.pending:aa", ModuleKey: "media", Type: "media.import.pending",
            Severity: severity, Title: "Import en attente", Body: "Manual Import required.",
            Data: null, State: state, OpenedAt: Start,
            LastSeenAt: Start + (duration ?? TimeSpan.Zero), ResolvedAt: null,
            SnoozedUntil: snoozedUntil, Occurrences: 1);

    [Fact]
    public void Une_anomalie_ouverte_critique_porte_licone_rouge_et_sa_duree()
    {
        var text = DiscordAnomalyRenderer.Render(
            Anomaly(severity: HubEventSeverity.Critical, duration: TimeSpan.FromHours(10)));

        Assert.Contains("🔴", text, StringComparison.Ordinal);
        Assert.Contains("Import en attente", text, StringComparison.Ordinal);
        Assert.Contains("Manual Import required.", text, StringComparison.Ordinal);
        Assert.Contains("Ouverte depuis 10 h", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Une_anomalie_ouverte_en_avertissement_porte_licone_orange()
    {
        var text = DiscordAnomalyRenderer.Render(Anomaly(severity: HubEventSeverity.Warning));

        Assert.Contains("⚠️", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Une_anomalie_resolue_le_dit_explicitement()
    {
        var text = DiscordAnomalyRenderer.Render(Anomaly(AnomalyState.Resolved));

        Assert.Contains("✅", text, StringComparison.Ordinal);
        Assert.Contains("Résolue", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Une_anomalie_en_sommeil_avec_echeance_affiche_lheure()
    {
        var until = new DateTimeOffset(2026, 8, 12, 16, 30, 0, TimeSpan.Zero);

        var text = DiscordAnomalyRenderer.Render(Anomaly(AnomalyState.Snoozed, snoozedUntil: until));

        Assert.Contains("😴", text, StringComparison.Ordinal);
        Assert.Contains("16:30", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Une_anomalie_en_sommeil_jusqua_resolution_ne_montre_aucune_heure()
    {
        var text = DiscordAnomalyRenderer.Render(Anomaly(AnomalyState.Snoozed, snoozedUntil: null));

        Assert.Contains("jusqu'à sa résolution", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_corps_absent_ne_laisse_pas_de_ligne_vide()
    {
        var anomaly = Anomaly() with { Body = null };

        var text = DiscordAnomalyRenderer.Render(anomaly);
        var lines = text.Split('\n');

        Assert.DoesNotContain("", lines);
    }
}
