using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Discord;
using Xunit;

namespace HomelabHub.Discord.Tests;

/// <summary>
/// Le rendu d'un <see cref="CapabilityResult"/> pour une réponse Discord.
/// </summary>
/// <remarks>
/// Corrige un bug trouvé en conditions réelles : <c>/system status</c> et <c>/media queue</c>
/// — deux <c>Query</c>, qui ne posent jamais de <c>Message</c>, seulement un <c>Payload</c> —
/// répondaient « Fait. » sans rien montrer. La distinction Query/Mutation gouvernait déjà
/// l'autorisation ; elle n'était simplement jamais lue ici.
/// </remarks>
public sealed class DiscordGatewayServiceRenderTests
{
    [Fact]
    public void Un_message_explicite_est_toujours_prioritaire_sur_la_charge_utile()
    {
        var result = CapabilityResult.Ok("Import demandé.", new { peu = "importe" });

        Assert.Equal("Import demandé.", DiscordGatewayService.Render(result, "Média"));
    }

    [Fact]
    public void Sans_message_une_charge_media_overview_obtient_le_rendu_dedie()
    {
        var overview = new HomelabHub.Modules.Media.Correlation.MediaOverview(
            [], 0, 0, 0, 0, 0, 0, 0, ObservedAt: null, []);

        var text = DiscordGatewayService.Render(CapabilityResult.Ok(overview), "Média");

        // Le test qui compte : ce n'est pas « Fait. ». La forme exacte est couverte par
        // DiscordWidgetRendererTests.
        Assert.NotEqual("Fait.", text);
        Assert.Contains("Aucune observation", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Sans_message_une_charge_quelconque_retombe_sur_le_repli_generique_avec_titre()
    {
        var text = DiscordGatewayService.Render(
            CapabilityResult.Ok(new { version = "0.1.0" }), "État du hub");

        Assert.Contains("État du hub", text, StringComparison.Ordinal);
        Assert.Contains("version", text, StringComparison.Ordinal);
        Assert.Contains("0.1.0", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Sans_message_ni_charge_le_repli_reste_fait_ou_transmise_selon_lissue()
    {
        Assert.Equal("Fait.", DiscordGatewayService.Render(CapabilityResult.Ok(), "X"));
        Assert.Equal("Opération transmise.",
            DiscordGatewayService.Render(new CapabilityResult(CapabilityOutcome.Accepted), "X"));
    }
}
