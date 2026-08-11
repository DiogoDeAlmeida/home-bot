namespace HomelabHub.Abstractions.Configuration;

/// <summary>
/// Lecture de la configuration d'un module, déjà déchiffrée et portée sur les clés
/// <i>relatives</i> déclarées dans son <see cref="ModuleConfigSchema"/>.
/// </summary>
/// <remarks>
/// <para>
/// Le paramètre de type sert uniquement à router vers le bon module : injecter
/// <c>IModuleConfiguration&lt;SystemModule&gt;</c> donne accès aux clés de <c>system</c>, sans
/// que le module ait à répéter sa propre clé à chaque lecture ni à pouvoir lire celles d'un
/// autre module.
/// </para>
/// <para>
/// Les valeurs sont relues à chaque appel : une modification depuis l'interface web prend effet
/// sans redémarrage ([ADR-0002]). Ne pas mettre en cache le résultat dans un champ.
/// </para>
/// </remarks>
public interface IModuleConfiguration<TModule> where TModule : IHubModuleMarker
{
    /// <summary>Valeur brute, ou <c>null</c> si absente et sans valeur par défaut au schéma.</summary>
    string? GetString(string key);

    /// <summary>Valeur booléenne, ou <paramref name="fallback"/> si absente ou illisible.</summary>
    bool GetBoolean(string key, bool fallback = false);

    /// <summary>Valeur entière, ou <paramref name="fallback"/> si absente ou illisible.</summary>
    int GetInt32(string key, int fallback = 0);

    /// <summary>Durée, ou <paramref name="fallback"/> si absente ou illisible.</summary>
    TimeSpan GetDuration(string key, TimeSpan fallback);

    /// <summary>
    /// Indique si tous les champs marqués obligatoires au schéma sont renseignés. Le noyau
    /// refuse d'activer un module dont la configuration est incomplète.
    /// </summary>
    bool IsComplete { get; }
}

/// <summary>
/// Contrainte de type pour <see cref="IModuleConfiguration{TModule}"/>.
/// </summary>
/// <remarks>
/// <see cref="Modules.IHubModule"/> ne peut pas servir directement de contrainte : il porte
/// des membres d'instance, or le paramètre de type ne désigne ici qu'un module, jamais une
/// instance à appeler. Ce marqueur exprime la contrainte sans cette ambiguïté.
/// </remarks>
public interface IHubModuleMarker;
