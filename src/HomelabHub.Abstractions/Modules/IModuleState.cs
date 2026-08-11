namespace HomelabHub.Abstractions.Modules;

/// <summary>
/// Snapshot immuable d'un module : le point où convergent les trois modes d'ingestion,
/// et la source unique lue par les widgets, les capacités, SignalR et le dashboard Discord.
/// </summary>
/// <remarks>
/// <para>
/// <b>Modèle de concurrence (ADR-0009).</b> Trois écrivains peuvent viser le même
/// snapshot simultanément : un poller sur son minuteur, un webhook sur une requête
/// entrante, un flux sur son propre thread. <see cref="Mutate"/> implémente un échange
/// atomique par <c>Interlocked.CompareExchange</c> avec réessai — pas de verrou, donc
/// pas de risque d'interblocage entre un flux bloquant et un poller.
/// </para>
/// <para>
/// <b>Conséquence pour l'appelant :</b> la fonction passée à <see cref="Mutate"/> doit
/// être <b>pure</b>. Elle peut être invoquée plusieurs fois si un autre écrivain gagne
/// la course. Elle ne doit donc ni écrire en base, ni publier d'événement, ni incrémenter
/// un compteur externe. Elle prend l'ancien snapshot, en renvoie un nouveau, et c'est tout.
/// </para>
/// <para>
/// Le noyau ne notifie les abonnés que si la référence a effectivement changé. Renvoyer
/// l'instance reçue est le moyen explicite de dire « rien de neuf » : aucun rendu Discord
/// ni aucune trame SignalR ne sera émis.
/// </para>
/// </remarks>
public interface IModuleState<TSnapshot> where TSnapshot : class
{
    /// <summary>Snapshot courant. Toujours non nul : le module fournit une valeur initiale.</summary>
    TSnapshot Current { get; }

    /// <summary>
    /// Applique une transformation pure et publie le résultat s'il diffère de l'existant.
    /// </summary>
    /// <param name="update">
    /// Transformation pure, potentiellement rejouée en cas de contention. Renvoyer
    /// l'instance reçue signifie « pas de changement » et n'émet aucune notification.
    /// </param>
    /// <returns>Le snapshot effectivement publié.</returns>
    TSnapshot Mutate(Func<TSnapshot, TSnapshot> update);

    /// <summary>
    /// S'abonne aux changements. Le noyau s'en sert pour alimenter SignalR et le dashboard
    /// Discord ; un module n'a normalement pas à l'appeler.
    /// </summary>
    /// <remarks>
    /// Les rappels sont invoqués sur le thread de l'écrivain : ils doivent être brefs et
    /// ne jamais bloquer. La diffusion réseau, elle, est débattue par le noyau.
    /// </remarks>
    IDisposable Subscribe(Action<TSnapshot> onChanged);
}
