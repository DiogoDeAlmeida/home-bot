using HomelabHub.Abstractions.Configuration;

namespace HomelabHub.Abstractions.Modules;

/// <summary>
/// Métadonnées et point d'enregistrement d'un domaine fonctionnel (média, domotique,
/// infrastructure…).
/// </summary>
/// <remarks>
/// <para>
/// <b>Contrainte d'instanciation :</b> le noyau instancie les implémentations
/// <i>avant</i> la construction du conteneur d'injection de dépendances. Une
/// implémentation ne doit donc porter aucune dépendance injectée, aucun état mutable,
/// et ne faire aucun appel réseau. Tout ce qui a besoin du conteneur passe par
/// <see cref="Register"/> puis se résout normalement à l'exécution.
/// </para>
/// <para>
/// <b>Activation :</b> tous les modules s'enregistrent au démarrage, activés ou non —
/// le conteneur .NET est immuable après <c>Build()</c>. L'activation est un état
/// runtime lu par le noyau, qui décide de démarrer les tâches d'ingestion, de publier
/// les capacités et de router les widgets. Voir ADR-0002.
/// </para>
/// </remarks>
public interface IHubModule : Configuration.IHubModuleMarker
{
    /// <summary>
    /// Identifiant stable du module : minuscules, <c>[a-z0-9-]</c>, 20 caractères maximum.
    /// </summary>
    /// <remarks>
    /// Cette clé préfixe absolument tout : clés de configuration, route de webhook
    /// (<c>/api/webhooks/{Key}/{hook}</c>), <c>custom_id</c> Discord, nom de la commande
    /// racine (<c>/media</c>, <c>/system</c>), clés de capacités. La changer casse la
    /// configuration persistée et les intégrations déjà déclarées côté services externes.
    /// À traiter comme une clé primaire.
    /// </remarks>
    string Key { get; }

    /// <summary>Nom affiché dans l'interface web et Discord. En français.</summary>
    string DisplayName { get; }

    /// <summary>Phrase courte décrivant le domaine couvert. En français.</summary>
    string Description { get; }

    /// <summary>
    /// Décrit la configuration attendue par le module.
    /// </summary>
    /// <remarks>
    /// Sert à trois usages distincts : générer le formulaire React sans écrire de code
    /// front, valider les valeurs côté serveur, et identifier les champs secrets à
    /// chiffrer au repos et à masquer dans les réponses de l'API.
    /// </remarks>
    ModuleConfigSchema ConfigSchema { get; }

    /// <summary>
    /// Déclare les services, sources d'ingestion, capacités et widgets du module.
    /// Appelé une seule fois au démarrage, pour tous les modules, activés ou non.
    /// </summary>
    void Register(IModuleRegistrationContext context);
}
