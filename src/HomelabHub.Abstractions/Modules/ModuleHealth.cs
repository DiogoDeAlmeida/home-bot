namespace HomelabHub.Abstractions.Modules;

/// <summary>
/// Sonde de santé d'un module. Séparée de <see cref="IHubModule"/> parce qu'elle a besoin
/// des clients résolus par le conteneur d'injection de dépendances.
/// </summary>
public interface IModuleHealthCheck
{
    /// <summary>
    /// Vérifie que le module peut joindre ses services. Ne doit jamais lever d'exception :
    /// un échec se traduit par <see cref="HealthState.Unhealthy"/> et un message lisible.
    /// </summary>
    Task<ModuleHealth> CheckAsync(CancellationToken cancellationToken);
}

/// <summary>Résultat d'une vérification de santé, agrégé et détaillé par service.</summary>
/// <param name="State">État global, généralement le pire des <paramref name="Services"/>.</param>
/// <param name="Message">Explication courte destinée à l'utilisateur, en français.</param>
/// <param name="Services">
/// Détail par service externe. C'est ce qui permet d'afficher « Radarr joignable,
/// qBittorrent injoignable » plutôt qu'un « module en erreur » inexploitable.
/// </param>
/// <param name="CheckedAt">Horodatage de la vérification.</param>
public sealed record ModuleHealth(
    HealthState State,
    string? Message,
    IReadOnlyList<ServiceHealth> Services,
    DateTimeOffset CheckedAt)
{
    public static ModuleHealth Disabled(DateTimeOffset at) =>
        new(HealthState.Disabled, "Module désactivé.", [], at);
}

/// <summary>Santé d'un service externe interrogé par un module.</summary>
/// <param name="Name">Nom affiché, par exemple « Radarr ».</param>
/// <param name="State">État constaté.</param>
/// <param name="Message">Cause de l'anomalie, le cas échéant.</param>
/// <param name="Latency">Temps de réponse mesuré, si la sonde a abouti.</param>
public sealed record ServiceHealth(
    string Name,
    HealthState State,
    string? Message = null,
    TimeSpan? Latency = null);

public enum HealthState
{
    /// <summary>Module activé mais pas encore sondé.</summary>
    Unknown = 0,

    /// <summary>Tout répond.</summary>
    Healthy = 1,

    /// <summary>Fonctionne partiellement : un service secondaire est tombé, l'essentiel tient.</summary>
    Degraded = 2,

    /// <summary>Le module ne peut pas rendre son service.</summary>
    Unhealthy = 3,

    /// <summary>Désactivé depuis l'interface : ni sondé, ni signalé comme problème.</summary>
    Disabled = 4,
}
