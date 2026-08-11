using System.Reflection;

namespace HomelabHub.Infrastructure;

/// <summary>
/// Ancre typée vers cet assembly.
/// </summary>
/// <remarks>
/// Étape 0 : vide. Accueillera EF Core et SQLite, la protection des données, la fabrique de
/// clients HTTP résilients, le fournisseur de configuration adossé à la base, le puits de
/// journalisation et la routine de sauvegarde.
/// </remarks>
public static class InfrastructureAssembly
{
    /// <summary>L'assembly d'infrastructure.</summary>
    public static Assembly Value => typeof(InfrastructureAssembly).Assembly;
}
