namespace HomelabHub.Discord;

/// <summary>
/// État de la passerelle Discord, consultable sans toucher au client Discord.Net lui-même.
/// </summary>
/// <remarks>
/// Née d'une question posée en revue, avant de bâtir un rollback de mise à jour sur
/// <c>/healthz</c> : que vérifie la sonde aujourd'hui ? Réponse honnête à l'époque — rien
/// d'autre que « le processus répond aux requêtes HTTP ». Un hub dont la connexion Discord
/// échoue en silence répondait pourtant <c>200</c>. Cette interface est ce qui permet à
/// <c>/healthz</c> de poser la vraie question.
/// </remarks>
public interface IDiscordConnectionStatus
{
    DiscordConnectionState State { get; }

    /// <summary>Détail lisible de l'état courant — raison d'un échec, ou <c>null</c> sinon.</summary>
    string? Detail { get; }
}

public enum DiscordConnectionState
{
    /// <summary>Aucun jeton ni serveur en configuration : l'adaptateur n'a jamais tenté de se connecter.</summary>
    NotConfigured = 0,

    /// <summary>Connexion ou reconnexion en cours.</summary>
    Connecting = 1,

    /// <summary><c>Ready</c> reçu, commandes enregistrées, serveur résolu.</summary>
    Connected = 2,

    /// <summary>La connexion ou l'enregistrement des commandes a échoué.</summary>
    Failed = 3,
}
