namespace HomelabHub.Abstractions.Capabilities;

/// <summary>
/// Une opération nommée, invocable depuis l'API REST et/ou Discord.
/// </summary>
/// <remarks>
/// C'est l'unité d'écriture d'une fonctionnalité : on l'écrit une fois, les adaptateurs
/// l'exposent. Ils la <i>découvrent</i>, mais ne la <i>devinent</i> pas — l'exposition
/// Discord est déclarée explicitement (ADR-0004), parce que les contraintes de Discord
/// sur les noms, les descriptions et les types d'arguments ne se satisfont pas d'une
/// projection automatique.
/// </remarks>
public interface IHubCapability
{
    /// <summary>Description statique, lue au démarrage pour construire routes et commandes.</summary>
    CapabilityDescriptor Descriptor { get; }

    /// <summary>
    /// Exécute l'opération. L'autorisation a déjà été vérifiée par le noyau : une
    /// implémentation n'a pas à contrôler qui l'appelle.
    /// </summary>
    Task<CapabilityResult> ExecuteAsync(CapabilityInvocation invocation,
                                        CancellationToken cancellationToken);
}
