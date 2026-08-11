using System.Reflection;

namespace HomelabHub.Modules.Media;

/// <summary>
/// Ancre typée vers cet assembly.
/// </summary>
/// <remarks>
/// <para>Étape 0 : vide. Contenu prévu à l'étape 3 :</para>
/// <list type="bullet">
///   <item>clients typés Seerr 3.4.1, Radarr 6.3.0, Sonarr 4.0.19, qBittorrent 5.1.0 ;</item>
///   <item>corrélation <c>MediaJourney (1) ── (0..N) DownloadItem</c>, jointure Seerr →
///         *arr par <c>externalServiceId</c>, jointure *arr → qBittorrent par
///         <c>downloadId</c> normalisé en minuscules ;</item>
///   <item>détecteurs d'anomalie, dont <c>media.import.manual-required</c> câblé directement
///         sur le déclencheur « Manual Interaction Required » plutôt que déduit d'un
///         <c>importPending</c> qui traîne.</item>
/// </list>
/// <para>
/// Les modèles de désérialisation seront écrits contre des réponses réellement capturées sur
/// l'installation cible, jamais contre une documentation : Radarr est en v6 majeure, et une
/// forme de réponse supposée d'après la v5 est exactement le genre d'erreur qui coûte une
/// soirée.
/// </para>
/// </remarks>
public static class MediaModuleAssembly
{
    /// <summary>L'assembly du module média.</summary>
    public static Assembly Value => typeof(MediaModuleAssembly).Assembly;
}
