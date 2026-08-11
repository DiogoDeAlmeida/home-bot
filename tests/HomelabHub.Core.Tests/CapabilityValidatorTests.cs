using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Core.Capabilities;
using Xunit;

namespace HomelabHub.Core.Tests;

/// <summary>
/// Le validateur doit casser au démarrage plutôt que produire une commande silencieusement
/// absente.
/// </summary>
public sealed class CapabilityValidatorTests
{
    private static CapabilityDescriptor Valid(
        string key = "system.status",
        CapabilityExposure exposure = CapabilityExposure.All,
        CommandBinding? command = null,
        string description = "Version et disponibilité.",
        IReadOnlyList<CapabilityParameter>? parameters = null) =>
        new(key, "État", description, parameters ?? [], CapabilityKind.Query, exposure, command);

    [Fact]
    public void Une_capacite_bien_declaree_ne_produit_aucune_erreur()
    {
        var errors = CapabilityValidator.Validate("system", Valid(command: new CommandBinding("status")));

        Assert.Empty(errors);
    }

    [Fact]
    public void Une_commande_sur_une_capacite_restreinte_a_lAPI_est_refusee()
    {
        // Le cas exact de system.backup.create : l'archive contient le keyring, donc de quoi
        // déchiffrer toutes les clés d'API. Deux déclarations qui se contredisent doivent
        // casser bruyamment, pas être arbitrées en silence (ADR-0004).
        var errors = CapabilityValidator.Validate("system",
            Valid(key: "system.backup.create",
                  exposure: CapabilityExposure.Api,
                  command: new CommandBinding("backup", "create")));

        Assert.Contains(errors, e => e.Contains("exclut les canaux conversationnels", StringComparison.Ordinal));
    }

    [Fact]
    public void Une_cle_non_prefixee_par_le_module_est_refusee()
    {
        var errors = CapabilityValidator.Validate("system", Valid(key: "media.queue.list"));

        Assert.Contains(errors, e => e.Contains("préfixée", StringComparison.Ordinal));
    }

    [Fact]
    public void Une_description_trop_longue_est_refusee_si_la_capacite_est_une_commande()
    {
        var errors = CapabilityValidator.Validate("system",
            Valid(description: new string('a', 101), command: new CommandBinding("status")));

        Assert.Contains(errors, e => e.Contains("101 caractères", StringComparison.Ordinal));
    }

    [Fact]
    public void Une_description_trop_longue_passe_si_la_capacite_reste_sur_lAPI()
    {
        // La limite de 100 caractères vient des plateformes conversationnelles, pas du hub.
        var errors = CapabilityValidator.Validate("system",
            Valid(description: new string('a', 300), exposure: CapabilityExposure.Api));

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
    public void Un_segment_de_commande_en_majuscules_est_refuse()
    {
        var errors = CapabilityValidator.Validate("system", Valid(command: new CommandBinding("Status")));

        Assert.Contains(errors, e => e.Contains("segment de commande", StringComparison.Ordinal));
    }

    [Fact]
    public void Un_chemin_de_commande_vide_est_refuse()
    {
        var errors = CapabilityValidator.Validate("system", Valid(command: new CommandBinding()));

        Assert.Contains(errors, e => e.Contains("chemin de commande vide", StringComparison.Ordinal));
    }

    [Fact]
    public void Un_chemin_de_commande_trop_profond_est_refuse()
    {
        // Deux segments hors clé de module : c'est la contrainte la plus stricte connue
        // (Discord plafonne à trois niveaux au total). Elle migrera dans l'adaptateur.
        var errors = CapabilityValidator.Validate("system",
            Valid(command: new CommandBinding("queue", "items", "pause")));

        Assert.Contains(errors, e => e.Contains("3 segments", StringComparison.Ordinal));
    }

    [Fact]
    public void Un_chemin_a_deux_segments_est_accepte()
    {
        var errors = CapabilityValidator.Validate("system",
            Valid(command: new CommandBinding("backup", "create")));

        Assert.Empty(errors);
    }
}
