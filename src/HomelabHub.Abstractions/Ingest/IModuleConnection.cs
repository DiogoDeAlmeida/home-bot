namespace HomelabHub.Abstractions.Ingest;

/// <summary>
/// Connexion longue durée tenue par le module. Troisième mode d'ingestion, et celui qui a
/// justifié la conception : sans lui, la WebSocket Home Assistant aurait imposé de rouvrir
/// le contrat à l'étape 7.
/// </summary>
/// <remarks>
/// <para>
/// <b>Partage des rôles.</b> Le module écrit une boucle de lecture ; le noyau écrit la
/// politique. Le noyau démarre la connexion à l'activation du module, l'annule à la
/// désactivation, la rétablit avec backoff exponentiel et gigue, compte les échecs
/// consécutifs et les remonte dans la santé. Une implémentation qui contient sa propre boucle
/// de reconnexion duplique le noyau et lui ment sur son état.
/// </para>
/// <para>
/// <b>Sortie identique aux autres modes :</b> une connexion écrit dans le même
/// <c>IModuleState&lt;T&gt;</c> et publie dans le même flux d'événements qu'un poller. Les
/// consommateurs — widgets, SignalR, dashboard Discord — ne savent pas d'où vient la donnée.
/// </para>
/// <para>
/// Le nom porte sur le cycle de vie plutôt que sur les données qui transitent, parce que
/// c'est le cycle de vie que cette abstraction prend en charge.
/// </para>
/// </remarks>
public interface IModuleConnection
{
    /// <summary>
    /// Établit la connexion et lit jusqu'à annulation. Sortir de cette méthode — normalement
    /// ou par exception — signale une déconnexion : le noyau replanifiera avec backoff.
    /// </summary>
    Task RunAsync(IConnectionContext context, CancellationToken cancellationToken);
}

/// <summary>Canal de retour d'une connexion vers le noyau.</summary>
public interface IConnectionContext
{
    /// <summary>
    /// Signale que la connexion est établie et opérationnelle.
    /// </summary>
    /// <remarks>
    /// Remet le compteur de backoff à zéro et bascule la santé en
    /// <see cref="Modules.HealthState.Healthy"/>. Sans cet appel, une connexion qui tient dix
    /// minutes puis tombe verrait son délai de rétablissement croître indéfiniment, alors
    /// qu'elle fonctionne l'essentiel du temps.
    /// </remarks>
    void ReportConnected();

    /// <summary>
    /// Signale une déconnexion volontaire ou détectée, avec sa cause. Optionnel : sortir de
    /// <see cref="IModuleConnection.RunAsync"/> suffit, mais renseigner la raison rend le
    /// journal exploitable sans SSH.
    /// </summary>
    void ReportDisconnected(string reason);
}
