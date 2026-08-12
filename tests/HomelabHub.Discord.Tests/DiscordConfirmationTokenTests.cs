using HomelabHub.Discord.Interactions;
using Xunit;

namespace HomelabHub.Discord.Tests;

/// <summary>
/// Le <c>custom_id</c> qui porte une confirmation en attente d'un clic.
/// </summary>
/// <remarks>
/// Sans état côté serveur (voir la remarque du type) : ces tests n'ont donc besoin d'aucune
/// passerelle, ni d'aucun état partagé entre l'encodage et le décodage — exactement ce qu'un
/// redémarrage du hub entre l'affichage du bouton et le clic doit pouvoir traverser.
/// </remarks>
public sealed class DiscordConfirmationTokenTests
{
    [Fact]
    public void Encoder_puis_decoder_redonne_la_capacite_et_ses_arguments()
    {
        var id = DiscordConfirmationToken.Encode("media.import.manual",
            new Dictionary<string, object?> { ["download"] = "481b6e3617be4c88f96cb25e47c9d8272130071e" });

        var decoded = DiscordConfirmationToken.TryDecode(id);

        Assert.NotNull(decoded);
        Assert.Equal("media.import.manual", decoded.Value.CapabilityKey);
        Assert.Equal("481b6e3617be4c88f96cb25e47c9d8272130071e", decoded.Value.Arguments["download"]);
    }

    [Fact]
    public void Sans_argument_le_jeton_reste_decodable_avec_des_arguments_vides()
    {
        var id = DiscordConfirmationToken.Encode("media.download.pause", new Dictionary<string, object?>());

        var decoded = DiscordConfirmationToken.TryDecode(id);

        Assert.NotNull(decoded);
        Assert.Empty(decoded.Value.Arguments);
    }

    [Fact]
    public void Un_argument_nul_est_omis_plutot_que_de_traverser_comme_chaine_vide()
    {
        var id = DiscordConfirmationToken.Encode("media.import.manual",
            new Dictionary<string, object?> { ["download"] = "aa", ["absent"] = null });

        var decoded = DiscordConfirmationToken.TryDecode(id);

        Assert.DoesNotContain("absent", decoded!.Value.Arguments.Keys);
    }

    [Fact]
    public void Des_caracteres_speciaux_dans_un_argument_survivent_lallée_retour()
    {
        // Un downloadId réel est un hash, sans surprise — mais rien n'empêche un futur argument
        // texte de contenir '&', '=' ou '?', qui sont justement les séparateurs de l'encodage.
        var id = DiscordConfirmationToken.Encode("media.import.manual",
            new Dictionary<string, object?> { ["download"] = "a&b=c?d e" });

        var decoded = DiscordConfirmationToken.TryDecode(id);

        Assert.Equal("a&b=c?d e", decoded!.Value.Arguments["download"]);
    }

    [Fact]
    public void Le_bouton_dannulation_nest_jamais_pris_pour_une_confirmation()
    {
        Assert.Null(DiscordConfirmationToken.TryDecode(DiscordConfirmationToken.Cancel));
    }

    [Fact]
    public void Un_identifiant_etranger_ne_se_decode_pas()
    {
        // Un bouton d'une version antérieure du hub, ou d'un tout autre composant Discord.
        Assert.Null(DiscordConfirmationToken.TryDecode("media.queue.details"));
        Assert.Null(DiscordConfirmationToken.TryDecode(""));
    }

    [Fact]
    public void Un_argument_trop_long_leve_plutot_que_de_produire_un_bouton_casse()
    {
        var enormous = new string('a', 200);

        Assert.Throws<InvalidOperationException>(() =>
            DiscordConfirmationToken.Encode("media.import.manual",
                new Dictionary<string, object?> { ["download"] = enormous }));
    }
}
