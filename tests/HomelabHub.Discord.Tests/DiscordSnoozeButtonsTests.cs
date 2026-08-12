using HomelabHub.Discord.Interactions;
using HomelabHub.Discord.Notifications;
using Xunit;

namespace HomelabHub.Discord.Tests;

/// <summary>
/// Les deux boutons de sommeil d'un message d'anomalie — séparés du mécanisme de confirmation
/// parce que <c>hub.anomaly.snooze</c> exécute directement, sans étape intermédiaire.
/// </summary>
public sealed class DiscordSnoozeButtonsTests
{
    [Fact]
    public void Encoder_puis_decoder_avec_une_duree_redonne_la_cle_et_les_heures()
    {
        var id = DiscordSnoozeButtons.Encode("media.import.pending:aa", hours: 6);

        var decoded = DiscordSnoozeButtons.TryDecode(id);

        Assert.NotNull(decoded);
        Assert.Equal("media.import.pending:aa", decoded.Value.DedupeKey);
        Assert.Equal(6, decoded.Value.Hours);
    }

    [Fact]
    public void Sans_duree_cest_jusqua_resolution_et_les_heures_sont_absentes()
    {
        var id = DiscordSnoozeButtons.Encode("media.import.pending:aa", hours: null);

        var decoded = DiscordSnoozeButtons.TryDecode(id);

        Assert.NotNull(decoded);
        Assert.Null(decoded.Value.Hours);
    }

    [Fact]
    public void Une_cle_de_deduplication_avec_deux_points_survit_a_lallée_retour()
    {
        // Le cas réel : une clé de détecteur porte toujours au moins un ':' avant le hash.
        var id = DiscordSnoozeButtons.Encode("media.download.stalled:481b6e3617be4c88f96cb25e47c9d827213", hours: 6);

        var decoded = DiscordSnoozeButtons.TryDecode(id);

        Assert.Equal("media.download.stalled:481b6e3617be4c88f96cb25e47c9d827213", decoded!.Value.DedupeKey);
    }

    [Fact]
    public void Le_bouton_dannulation_de_la_confirmation_nest_pas_pris_pour_un_sommeil()
    {
        Assert.Null(DiscordSnoozeButtons.TryDecode(DiscordConfirmationToken.Cancel));
    }

    [Fact]
    public void Un_bouton_de_confirmation_nest_pas_pris_pour_un_sommeil()
    {
        var confirmId = DiscordConfirmationToken.Encode("media.import.manual",
            new Dictionary<string, object?> { ["download"] = "aa" });

        Assert.Null(DiscordSnoozeButtons.TryDecode(confirmId));
    }
}
