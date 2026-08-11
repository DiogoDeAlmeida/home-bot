namespace HomelabHub.Abstractions.Ingest;

/// <summary>
/// Réception d'une notification poussée par un service externe. Deuxième mode d'ingestion.
/// </summary>
/// <remarks>
/// <para>
/// Le noyau expose <c>POST /api/webhooks/{moduleKey}/{hook}</c>, authentifie l'appel,
/// applique une limitation de débit, refuse tant que l'assistant de premier démarrage n'est
/// pas terminé, puis route ici. Le gestionnaire ne voit ni jeton, ni route, ni contrôle
/// d'accès : uniquement un corps de requête à interpréter.
/// </para>
/// <para>
/// <b>Authentification (ADR-0012).</b> Mode nominal : en-tête <c>X-Hub-Token</c>, comparé en
/// temps constant — Radarr 6.3 et Sonarr 4.0.19 savent envoyer des en-têtes personnalisés
/// sur leurs connexions Webhook. Mode dégradé, pour les services qui ne le savent pas :
/// jeton en dernier segment d'URL. La vérification côté noyau est identique dans les deux
/// cas ; seule la source du jeton diffère. Le jeton n'apparaît jamais dans les journaux.
/// </para>
/// </remarks>
public interface IModuleWebhookHandler
{
    /// <summary>
    /// Interprète une notification entrante. Ne doit jamais lever d'exception : un corps
    /// illisible se traduit par <see cref="WebhookResult.Ignored"/> avec une raison.
    /// </summary>
    Task<WebhookResult> HandleAsync(WebhookRequest request, CancellationToken cancellationToken);
}

/// <summary>Notification entrante, déjà authentifiée par le noyau.</summary>
/// <param name="Hook">
/// Segment de route déclaré via <c>AddWebhook</c>, qui identifie la source (<c>radarr</c>,
/// <c>sonarr</c>, <c>seerr</c>…).
/// </param>
/// <param name="Body">Corps brut. Le module choisit son propre modèle de désérialisation.</param>
/// <param name="Headers">
/// En-têtes de la requête, expurgés des en-têtes d'authentification.
/// </param>
/// <param name="ReceivedAt">Horodatage de réception.</param>
public sealed record WebhookRequest(
    string Hook,
    string Body,
    IReadOnlyDictionary<string, string> Headers,
    DateTimeOffset ReceivedAt);

/// <summary>Issue du traitement d'une notification.</summary>
/// <param name="Accepted">
/// <c>false</c> si le corps était inexploitable. Journalisé, mais renvoie tout de même un
/// 200 au service appelant : Radarr désactive une connexion qui échoue trop souvent, et on
/// ne veut pas perdre l'intégration à cause d'un payload inattendu.
/// </param>
/// <param name="RequestRefresh">
/// Demande au noyau un cycle de poll anticipé, débattu pour absorber les rafales.
/// C'est le motif normal : un payload « Grab » signale qu'il s'est passé quelque chose,
/// mais ne contient pas l'état de la file. Le push donne la latence, le poll donne la vérité.
/// </param>
/// <param name="Reason">Explication en cas de rejet, pour le journal consultable dans l'UI.</param>
public sealed record WebhookResult(bool Accepted, bool RequestRefresh, string? Reason = null)
{
    /// <summary>Traité, et l'état doit être rafraîchi sans attendre le prochain cycle.</summary>
    public static WebhookResult AcceptedAndRefresh() => new(true, true);

    /// <summary>Traité, sans besoin de rafraîchir l'état.</summary>
    public static WebhookResult AcceptedOnly() => new(true, false);

    /// <summary>Reçu mais sans intérêt pour ce module (type d'événement non géré, test de connexion…).</summary>
    public static WebhookResult Ignored(string reason) => new(false, false, reason);
}
