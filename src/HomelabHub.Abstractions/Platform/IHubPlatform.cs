using HomelabHub.Abstractions.Configuration;

namespace HomelabHub.Abstractions.Platform;

/// <summary>
/// Services que la plateforme rend aux modules, au même titre que
/// <see cref="Events.IEventPublisher"/> ou <see cref="Modules.IModuleState{TSnapshot}"/>.
/// </summary>
/// <remarks>
/// Ils vivent dans <c>Abstractions</c> parce qu'un module doit pouvoir s'en servir sans
/// référencer le noyau (ADR-0010).
/// </remarks>
public interface IHubPlatform
{
    /// <summary>Version informationnelle du binaire en cours d'exécution.</summary>
    string Version { get; }

    /// <summary>Instant de démarrage du processus.</summary>
    DateTimeOffset StartedAt { get; }

    /// <summary>Répertoire des données persistantes — base, keyring, sauvegardes.</summary>
    string DataDirectory { get; }

    /// <summary>Répertoire de configuration.</summary>
    string ConfigDirectory { get; }
}

/// <summary>
/// Permet à un module de <b>demander</b> une sauvegarde. Rien de plus.
/// </summary>
/// <remarks>
/// <para>
/// La sauvegarde elle-même — création, restauration, accès aux archives — reste interne au
/// noyau (ADR-0014). L'archive contient le keyring, donc de quoi déchiffrer toutes les clés
/// d'API du homelab : en interdire le déclenchement depuis Discord tout en le rendant
/// résoluble par n'importe quel module rouvrirait l'accès par une autre porte.
/// </para>
/// <para>
/// Ce contrat n'exprime donc qu'une <i>intention</i>. Le noyau décide s'il l'honore, applique
/// un anti-rebond, et journalise l'appelant et le motif. Le paramètre de type suit la même
/// convention que <see cref="IModuleConfiguration{TModule}"/> : il donne au noyau l'identité de
/// l'appelant sans que le module ait à la déclarer.
/// </para>
/// </remarks>
public interface IBackupRequester<TModule> where TModule : IHubModuleMarker
{
    /// <param name="reason">Motif journalisé, en français, destiné à l'exploitant.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<BackupRequestResult> RequestBackupAsync(string reason, CancellationToken cancellationToken);
}

/// <param name="Outcome">Décision du noyau.</param>
/// <param name="Message">Explication destinée à l'utilisateur, en français.</param>
public sealed record BackupRequestResult(BackupRequestOutcome Outcome, string Message);

public enum BackupRequestOutcome
{
    /// <summary>Une archive a été produite.</summary>
    Created = 0,

    /// <summary>Refusée : une sauvegarde trop récente existe déjà (anti-rebond).</summary>
    Throttled = 1,

    /// <summary>La sauvegarde a échoué. Le détail est dans le journal, pas dans le message.</summary>
    Failed = 2,
}
