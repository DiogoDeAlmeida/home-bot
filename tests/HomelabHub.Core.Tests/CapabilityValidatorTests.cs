using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Core.Capabilities;
using Xunit;

namespace HomelabHub.Core.Tests;

/// <summary>
/// Le validateur doit casser au démarrage plutôt que produire une commande Discord
/// silencieusement absente.
/// </summary>
public sealed class CapabilityValidatorTests
{
    private static CapabilityDescriptor Valid(
        string key = "system.status",
        CapabilityExposure exposure = CapabilityExposure.All,
        DiscordBinding? discord = null,
        string description = "Version et disponibilité.",
        IReadOnlyList<CapabilityParameter>? parameters = null) =>
        new(key, "État", description, parameters ?? [], CapabilityKind.Query, exposure, discord);

    [Fact]
    public void Une_capacite_bien_declaree_ne_produit_aucune_erreur()
    {
        var errors = CapabilityValidator.Validate("system",
            Valid(discord: new DiscordBinding(null, "status")));

        Assert.Empty(errors);
    }

    [Fact]
    public void Un_binding_Discord_sur_une_capacite_restreinte_au_REST_est_refuse()
    {
        // Le cas exact de system.backup.create : l'archive contient le keyring, donc de quoi
        // déchiffrer toutes les clés d'API. Deux déclarations qui se contredisent doivent
        // casser bruyamment, pas être arbitrées en silence (ADR-0004).
        var errors = CapabilityValidator.Validate("system",
            Valid(key: "system.backup.create",
                  exposure: CapabilityExposure.Rest,
                  discord: new DiscordBinding("backup", "create")));

        Assert.Contains(errors, e => e.Contains("exclut Discord", StringComparison.Ordinal));
    }

    [Fact]
    public void Une_cle_non_prefixee_par_le_module_est_refusee()
    {
        var errors = CapabilityValidator.Validate("system", Valid(key: "media.queue.list"));

        Assert.Contains(errors, e => e.Contains("préfixée", StringComparison.Ordinal));
    }

    [Fact]
    public void Une_description_trop_longue_est_refusee_si_la_capacite_va_sur_Discord()
    {
        var errors = CapabilityValidator.Validate("system",
            Valid(description: new string('a', 101), discord: new DiscordBinding(null, "status")));

        Assert.Contains(errors, e => e.Contains("101 caractères", StringComparison.Ordinal));
    }

    [Fact]
    public void Une_description_trop_longue_passe_si_la_capacite_reste_en_REST()
    {
        // La limite de 100 caractères est une contrainte Discord, pas une règle du hub.
        var errors = CapabilityValidator.Validate("system",
            Valid(description: new string('a', 300), exposure: CapabilityExposure.Rest));

        Assert.Empty(errors);
    }

    [Fact]
    public void Un_parametre_obligatoire_apres_un_optionnel_est_refuse()
    {
        var errors = CapabilityValidator.Validate("system", Valid(parameters:
        [
            new CapabilityParameter("optionnel", "Facultatif", CapabilityParameterType.String),
            new CapabilityParameter("requis", "Obligatoire", CapabilityParameterType.String, Required: true),
        ]));

        Assert.Contains(errors, e => e.Contains("l'ordre inverse", StringComparison.Ordinal));
    }

    [Fact]
    public void Une_exposition_vide_est_refusee()
    {
        var errors = CapabilityValidator.Validate("system", Valid(exposure: CapabilityExposure.None));

        Assert.Contains(errors, e => e.Contains("injoignable", StringComparison.Ordinal));
    }

    [Fact]
    public void Un_nom_de_sous_commande_en_majuscules_est_refuse()
    {
        var errors = CapabilityValidator.Validate("system",
            Valid(discord: new DiscordBinding(null, "Status")));

        Assert.Contains(errors, e => e.Contains("nom de sous-commande", StringComparison.Ordinal));
    }
}
