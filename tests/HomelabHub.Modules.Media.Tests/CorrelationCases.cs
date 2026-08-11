using Xunit;

namespace HomelabHub.Modules.Media.Tests;

/// <summary>
/// Les quatre cas qui cassent le modèle naïf « une ligne = un média », écrits avant le code
/// de corrélation.
/// </summary>
/// <remarks>
/// <para>
/// La corrélation Seerr → Radarr/Sonarr → qBittorrent est le cœur du module et sa principale
/// difficulté. Les clés de jointure sont connues : <c>media.externalServiceId</c> côté Seerr,
/// et <c>downloadId</c> côté *arr, qui est le hash du torrent en majuscules là où qBittorrent
/// le renvoie en minuscules. Ce sont les cas <i>limites</i> qui déterminent la forme du modèle
/// de données, d'où leur présence ici dès l'étape 0.
/// </para>
/// <para>
/// Ces tests sont ignorés tant que le module n'existe pas. Ils ne sont pas des marque-pages :
/// ils énoncent le contrat que la corrélation devra tenir, et le fait qu'ils apparaissent
/// « ignorés » dans chaque exécution de la CI est délibéré — c'est une dette visible.
/// </para>
/// </remarks>
public sealed class CorrelationCases
{
    private const string NotImplementedYet =
        "Étape 3 — le module média n'est pas encore écrit.";

    /// <summary>
    /// Une requête de saison peut se résoudre en un seul torrent contenant tous les épisodes.
    /// Le modèle doit rattacher N épisodes Sonarr à un unique <c>DownloadItem</c>, sans
    /// dupliquer la ligne affichée ni compter la progression N fois.
    /// </summary>
    [Fact(Skip = NotImplementedYet)]
    public void Requete_de_saison_resolue_en_pack_unique_donne_un_seul_telechargement()
    {
    }

    /// <summary>
    /// Une requête de saison peut aussi se résoudre en un torrent par épisode. Même
    /// <c>MediaJourney</c>, N <c>DownloadItem</c>, et une progression agrégée qui a du sens
    /// pour l'utilisateur — pas une moyenne de pourcentages, une somme d'octets.
    /// </summary>
    [Fact(Skip = NotImplementedYet)]
    public void Requete_de_saison_resolue_en_episodes_separes_donne_N_telechargements_agreges()
    {
    }

    /// <summary>
    /// Un ajout manuel dans Radarr n'a aucune requête Seerr en amont. Le <c>MediaJourney</c>
    /// doit exister sans demandeur : la jointure amont est facultative, et son absence n'est
    /// pas une anomalie.
    /// </summary>
    [Fact(Skip = NotImplementedYet)]
    public void Import_manuel_sans_requete_Seerr_produit_un_parcours_sans_demandeur()
    {
    }

    /// <summary>
    /// Seerr peut marquer un média disponible sans qu'aucun téléchargement n'ait eu lieu,
    /// parce qu'il était déjà dans la bibliothèque. Parcours sans <c>DownloadItem</c>, et
    /// surtout aucune anomalie « bloqué » sur un téléchargement qui n'existe pas.
    /// </summary>
    [Fact(Skip = NotImplementedYet)]
    public void Media_deja_present_donne_un_parcours_disponible_sans_telechargement()
    {
    }

    /// <summary>
    /// Radarr peut abandonner une release et en reprendre une meilleure. L'ancien
    /// <c>downloadId</c> disparaît de la file, un nouveau apparaît sur le même média.
    /// Le parcours doit survivre au remplacement, et l'anomalie éventuellement ouverte sur
    /// l'ancien torrent doit être résolue, pas laissée orpheline.
    /// </summary>
    [Fact(Skip = NotImplementedYet)]
    public void Release_remplacee_ferme_lanomalie_de_lancien_telechargement()
    {
    }
}
