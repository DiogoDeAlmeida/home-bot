using System.Reflection;

namespace HomelabHub.Modules.SystemModule;

/// <summary>
/// Ancre typée vers cet assembly.
/// </summary>
/// <remarks>
/// Étape 0 : vide. Ce module est le <b>banc de test de l'abstraction</b> : réel, trivial et
/// utile en production (disponibilité, version, espace disque, santé du hub). Il sera écrit
/// en premier à l'étape 1, avant l'interface web, pour que le contrat soit confronté à une
/// implémentation avant d'être figé.
/// </remarks>
public static class SystemModuleAssembly
{
    /// <summary>L'assembly du module système.</summary>
    public static Assembly Value => typeof(SystemModuleAssembly).Assembly;
}
