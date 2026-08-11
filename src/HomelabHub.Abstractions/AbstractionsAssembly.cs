using System.Reflection;

namespace HomelabHub.Abstractions;

/// <summary>Ancre typée vers cet assembly, pour la réflexion et les tests d'architecture.</summary>
public static class AbstractionsAssembly
{
    /// <summary>L'assembly des contrats.</summary>
    public static Assembly Value => typeof(AbstractionsAssembly).Assembly;
}
