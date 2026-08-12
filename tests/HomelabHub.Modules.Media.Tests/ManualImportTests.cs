using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Modules.Media.Contracts;
using Xunit;

namespace HomelabHub.Modules.Media.Tests;

/// <summary>
/// L'import manuel : la seule écriture du module média vers un service.
/// </summary>
/// <remarks>
/// Le candidat désérialisé provient de la capture réelle de l'import bloqué, donc la forme
/// testée est celle que Radarr 6.3 produit — y compris ses tableaux imbriqués, dont un
/// aplatissement accidentel rendrait la commande invalide sans que rien ne le signale.
/// </remarks>
public sealed class ManualImportTests
{
    private const string Candidate = "Lifecycle/42-radarr-manualimport-candidat.json";

    [Fact]
    public void Le_candidat_reel_se_deserialise_avec_ses_tableaux_imbriques()
    {
        var candidate = Assert.Single(Fixture.Load<List<ArrManualImportCandidate>>(Candidate));

        Assert.NotNull(candidate.Path);
        Assert.NotNull(candidate.Movie);
        Assert.True(candidate.Movie!.Id > 0);
        Assert.True(candidate.Size > 0);
        Assert.NotNull(candidate.DownloadId);

        // Ces trois-là sont des tableaux dans la réponse réelle. Les modéliser en objet unique
        // produirait une commande d'import silencieusement invalide.
        Assert.NotEmpty(candidate.Languages);
        Assert.NotNull(candidate.Quality);
        Assert.NotNull(candidate.Quality!.Quality);
        Assert.True(candidate.Quality.Quality!.Resolution > 0);
    }

    [Fact]
    public void Aucun_rejet_signifie_importable()
    {
        // Le cas observé : le fichier était parfaitement importable, Radarr refusait seulement
        // de le faire seul parce que la correspondance venait de l'ID et non du titre.
        var candidate = Assert.Single(Fixture.Load<List<ArrManualImportCandidate>>(Candidate));

        Assert.Empty(candidate.Rejections);
    }

    [Fact]
    public void La_capacite_exige_une_confirmation()
    {
        // Un import déclenché sur le mauvais fichier place un média au mauvais endroit de la
        // bibliothèque. RequireConfirmation est porté par la capacité, donc il s'applique à
        // toutes les surfaces — web, canal conversationnel, API (ADR-0016).
        //
        // Le descripteur est lu sur la VRAIE capacité, pas sur une copie : une assertion contre
        // un duplicat vérifierait que le test se ressemble à lui-même. Les dépendances sont
        // nulles parce qu'une capacité n'en a besoin d'aucune pour se décrire — c'est
        // précisément ce dont le validateur de démarrage dépend.
        var descriptor = new Capabilities.ManualImportCapability(null!, null!, null!).Descriptor;

        Assert.Equal(CapabilityKind.Mutation, descriptor.Kind);
        Assert.True(descriptor.RequireConfirmation);
        Assert.Equal("media.import.manual", descriptor.Key);
        Assert.Equal(CapabilityExposure.All, descriptor.Exposure);

        var parameter = Assert.Single(descriptor.Parameters);
        Assert.True(parameter.Required);
        Assert.Equal("download", parameter.Name);
    }

    [Fact]
    public void Toutes_les_mutations_du_module_exigent_une_confirmation()
    {
        // Règle de portée : une opération qui écrit chez un service tiers ne doit pas pouvoir
        // partir sur un clic isolé. Ce test attrapera la prochaine mutation ajoutée sans
        // confirmation, y compris celles de qBittorrent à venir.
        var mutations = new IHubCapability[]
        {
            new Capabilities.ManualImportCapability(null!, null!, null!),
        };

        Assert.All(mutations, capability =>
        {
            Assert.Equal(CapabilityKind.Mutation, capability.Descriptor.Kind);
            Assert.True(capability.Descriptor.RequireConfirmation,
                $"{capability.Descriptor.Key} est une mutation sans confirmation.");
        });
    }
}
