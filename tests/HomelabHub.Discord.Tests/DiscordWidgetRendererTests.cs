using HomelabHub.Abstractions.Dashboard;
using HomelabHub.Discord.Dashboard;
using HomelabHub.Modules.Media.Correlation;
using Xunit;

namespace HomelabHub.Discord.Tests;

/// <summary>
/// Le rendu dédié du palmarès média, et le repli générique pour tout widget sans rendu propre
/// (ADR-0006).
/// </summary>
public sealed class DiscordWidgetRendererTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 14, 0, 0, TimeSpan.Zero);

    private static JourneySummary Journey(
        string title, bool needsAttention = false, double progress = 0.5,
        long downloadSpeed = 0, TimeSpan? eta = null,
        JourneyState state = JourneyState.Downloading) =>
        new("movie:1", title, MediaKind.Movie, state, needsAttention, progress,
            downloadSpeed, 0, eta, 1, 0, null, ["481b6e3617be4c88f96cb25e47c9d8272130071e"]);

    [Fact]
    public void Avant_le_premier_cycle_le_widget_le_dit_explicitement()
    {
        var overview = new MediaOverview([], 0, 0, 0, 0, 0, 0, 0, ObservedAt: null, []);

        var text = DiscordWidgetRenderer.Render(new WidgetPayload("media.overview", overview, Now));

        Assert.Contains("Aucune observation", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Une_entree_bloquee_affiche_bloque_plutot_quun_pourcentage()
    {
        var overview = new MediaOverview(
            [Journey("Avatar : The Last Airbender", needsAttention: true, progress: 0.3)],
            1, 1, 0, 1, 0, 0, 0, Now, []);

        var text = DiscordWidgetRenderer.Render(new WidgetPayload("media.overview", overview, Now));

        Assert.Contains("🔴 Avatar : The Last Airbender — bloqué", text, StringComparison.Ordinal);
        Assert.DoesNotContain("30%", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Une_entree_saine_affiche_pourcentage_debit_et_temps_restant()
    {
        var overview = new MediaOverview(
            [Journey("The Wild Robot", progress: 0.62, downloadSpeed: 3_200_000, eta: TimeSpan.FromMinutes(12))],
            1, 1, 0, 0, 3_200_000, 0, 0, Now, []);

        var text = DiscordWidgetRenderer.Render(new WidgetPayload("media.overview", overview, Now));

        Assert.Contains("62%", text, StringComparison.Ordinal);
        Assert.Contains("Mo/s", text, StringComparison.Ordinal);
        Assert.Contains("reste 12 min", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Une_entree_en_import_porte_licone_dediee()
    {
        var overview = new MediaOverview(
            [Journey("Debian 13", progress: 1.0, state: JourneyState.Importing)],
            1, 0, 1, 0, 0, 0, 0, Now, []);

        var text = DiscordWidgetRenderer.Render(new WidgetPayload("media.overview", overview, Now));

        Assert.Contains("📦 Debian 13", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Le_resume_chiffre_porte_les_quatre_compteurs()
    {
        var overview = new MediaOverview(
            [], 12, 2, 1, 1, 5_000_000, 1_500_000_000, 3_000_000_000, Now, []);

        var text = DiscordWidgetRenderer.Render(new WidgetPayload("media.overview", overview, Now));

        Assert.Contains("2 en cours", text, StringComparison.Ordinal);
        Assert.Contains("1 en import", text, StringComparison.Ordinal);
        Assert.Contains("1 à surveiller", text, StringComparison.Ordinal);
        Assert.Contains("restants", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_service_injoignable_reste_visible_dans_le_rendu()
    {
        var overview = new MediaOverview([], 0, 0, 0, 0, 0, 0, 0, Now, ["qbittorrent"]);

        var text = DiscordWidgetRenderer.Render(new WidgetPayload("media.overview", overview, Now));

        Assert.Contains("Injoignable", text, StringComparison.Ordinal);
        Assert.Contains("qbittorrent", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_widget_sans_rendu_dedie_retombe_sur_le_cle_valeur_generique()
    {
        var payload = new WidgetPayload("system.disk", new { path = "/mnt/data", freePercent = 12 }, Now);

        var text = DiscordWidgetRenderer.Render(payload);

        Assert.Contains("system.disk", text, StringComparison.Ordinal);
        Assert.Contains("path", text, StringComparison.Ordinal);
        Assert.Contains("/mnt/data", text, StringComparison.Ordinal);
    }
}
