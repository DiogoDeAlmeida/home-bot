using System.Reflection;

namespace HomelabHub.Modules.SystemInfo;

/// <summary>Ancre typée vers cet assembly, pour les tests d'architecture.</summary>
public static class SystemModuleAssembly
{
    /// <summary>L'assembly du module système.</summary>
    public static Assembly Value => typeof(SystemModuleAssembly).Assembly;
}
