namespace HomelabHub.Abstractions.Ingest;

/// <summary>
/// Interrogation périodique d'un service externe. Premier des trois modes d'ingestion.
/// </summary>
/// <remarks>
/// <para>
/// Le poller est la <b>source de vérité</b> de l'état d'un module. Les webhooks donnent
/// la réactivité, mais un webhook perdu ne doit jamais laisser le hub dans un état faux :
/// le cycle suivant répare. C'est ce qui autorise un intervalle sobre (60 s pour le média)
/// sans sacrifier la latence perçue.
/// </para>
/// <para>
/// Le noyau garantit qu'un seul cycle s'exécute à la fois pour un poller donné : une
/// implémentation n'a pas à se protéger d'un recouvrement. Une exception qui s'échappe est
/// journalisée et remontée dans la santé du module, mais n'interrompt pas la cadence et ne
/// fait jamais tomber le processus.
/// </para>
/// </remarks>
public interface IModulePoller
{
    /// <summary>
    /// Exécute un cycle : interroger, projeter dans le snapshot via
    /// <c>IModuleState&lt;T&gt;.Mutate</c>, publier les événements qui en découlent.
    /// </summary>
    Task PollAsync(CancellationToken cancellationToken);
}
