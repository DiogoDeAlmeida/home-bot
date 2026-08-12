namespace HomelabHub.Core.Capabilities;

/// <summary>
/// Capacités portées par le noyau lui-même, sans appartenir à un module.
/// </summary>
/// <remarks>
/// <para>
/// Le pendant, côté capacités, de ce qu'ADR-0013 fait déjà pour la configuration : le noyau
/// s'inscrit sous la clé réservée <c>hub</c>, comme un pseudo-module. La mise en sommeil d'une
/// anomalie est la première opération de ce genre — elle appartient au noyau (c'est
/// <see cref="Anomalies.AnomalyEngine"/> qui tient l'état), pas à un domaine précis. D'autres
/// suivront : purger une ligne de journal, forcer une resynchronisation.
/// </para>
/// <para>
/// Sans ce catalogue, une telle opération n'a que deux issues : devenir la capacité d'un module
/// qui n'a aucun rapport avec elle, ou contourner <see cref="ICapabilityExecutor"/> par un appel
/// direct — ce qui la prive de la journalisation, de la confirmation et de l'exposition REST que
/// toute autre mutation obtient gratuitement. Les deux sont pires que ce petit catalogue dédié.
/// </para>
/// </remarks>
internal sealed class HubCapabilityCatalog(IReadOnlyList<Type> types)
{
    public IReadOnlyList<Type> Types { get; } = types;
}
