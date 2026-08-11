using System.Reflection;

namespace HomelabHub.Core;

/// <summary>
/// Ancre typée vers cet assembly.
/// </summary>
/// <remarks>
/// Étape 0 : le noyau est vide. Il accueillera à l'étape 1 le registre des modules,
/// l'exécution des capacités, le magasin de configuration, l'implémentation de
/// <c>IModuleState&lt;T&gt;</c>, le moteur d'anomalies et l'ordonnanceur d'ingestion.
/// </remarks>
public static class CoreAssembly
{
    /// <summary>L'assembly du noyau.</summary>
    public static Assembly Value => typeof(CoreAssembly).Assembly;
}
