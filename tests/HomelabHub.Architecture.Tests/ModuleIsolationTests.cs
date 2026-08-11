using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace HomelabHub.Architecture.Tests;

/// <summary>
/// Garde la règle de dépendances énoncée dans ADR-0010.
/// </summary>
/// <remarks>
/// <para>
/// « Si intégrer Home Assistant demande de modifier le noyau, c'est que l'abstraction est
/// ratée. » Cette phrase du cadrage n'a de valeur que si quelque chose l'applique. Une
/// convention orale se viole en trois mois, sans mauvaise foi : on ajoute une référence pour
/// débloquer un cas, et la frontière disparaît. Ici, la CI casse.
/// </para>
/// <para>
/// <b>Pourquoi pas NetArchTest.</b> La règle porte sur les références entre projets, pas sur
/// des relations entre types. Deux vérifications suffisent, sans dépendance supplémentaire :
/// les <c>ProjectReference</c> déclarés dans les fichiers <c>.csproj</c> — qui expriment
/// l'intention, y compris pour une référence inutilisée que le compilateur éliderait — et les
/// références réellement présentes dans les assemblys compilés. NetArchTest deviendra utile le
/// jour où il faudra des règles plus fines (interdire tel espace de noms, imposer une
/// direction entre couches à l'intérieur du noyau).
/// </para>
/// </remarks>
public sealed class ModuleIsolationTests
{
    private const string AbstractionsProject = "HomelabHub.Abstractions";

    /// <summary>Assemblys de la solution, hors modules, qu'un module n'a pas le droit de connaître.</summary>
    private static readonly string[] ForbiddenForModules =
    [
        "HomelabHub.Core",
        "HomelabHub.Infrastructure",
        "HomelabHub.Discord",
        "HomelabHub.Host",
    ];

    [Fact]
    public void Un_module_ne_declare_de_reference_que_vers_Abstractions()
    {
        var moduleProjects = FindProjects("HomelabHub.Modules.*");

        Assert.NotEmpty(moduleProjects);

        foreach (var project in moduleProjects)
        {
            var references = ReadProjectReferences(project);

            Assert.All(references, reference =>
                Assert.True(
                    reference == AbstractionsProject,
                    $"""
                     {Path.GetFileName(project)} référence « {reference} ».

                     Un module ne référence que {AbstractionsProject} (ADR-0010).
                     Si l'abstraction ne suffit pas pour ce que tu veux faire, c'est
                     l'abstraction qu'il faut corriger — pas cette référence qu'il faut
                     ajouter. Ouvre un ADR avant de toucher à cette règle.
                     """));
        }
    }

    [Fact]
    public void Un_module_compile_ne_depend_daucun_assembly_interdit()
    {
        var moduleAssemblies = new[]
        {
            Modules.SystemModule.SystemModuleAssembly.Value,
            Modules.Media.MediaModuleAssembly.Value,
        };

        foreach (var assembly in moduleAssemblies)
        {
            var referenced = assembly.GetReferencedAssemblies()
                .Select(a => a.Name)
                .Where(name => name is not null)
                .ToArray();

            foreach (var forbidden in ForbiddenForModules)
            {
                Assert.DoesNotContain(forbidden, referenced);
            }
        }
    }

    [Fact]
    public void Abstractions_ne_reference_aucun_projet_de_la_solution()
    {
        var project = FindProjects(AbstractionsProject).Single();

        Assert.Empty(ReadProjectReferences(project));
    }

    [Fact]
    public void Abstractions_ne_depend_que_du_framework_et_de_Microsoft_Extensions()
    {
        var referenced = Abstractions.AbstractionsAssembly.Value
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => !name.StartsWith("System.", StringComparison.Ordinal)
                           && name != "System"
                           && name != "netstandard"
                           && !name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal))
            .ToArray();

        Assert.True(referenced.Length == 0,
            $"""
             {AbstractionsProject} a gagné des dépendances : {string.Join(", ", referenced)}.

             Tout ce qui entre ici est imposé à chaque module futur. Y réfléchir à deux fois.
             """);
    }

    // ── Utilitaires ──────────────────────────────────────────────────────────────────

    private static string[] FindProjects(string pattern) =>
        Directory.GetFiles(Path.Combine(RepositoryRoot(), "src"), $"{pattern}.csproj",
                           SearchOption.AllDirectories);

    private static string[] ReadProjectReferences(string csprojPath)
    {
        var directory = Path.GetDirectoryName(csprojPath)!;

        return XDocument.Load(csprojPath)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFileNameWithoutExtension(
                Path.GetFullPath(Path.Combine(directory, include!.Replace('\\', Path.DirectorySeparatorChar)))))
            .ToArray();
    }

    /// <summary>Remonte depuis le répertoire de sortie des tests jusqu'à la racine du dépôt.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Racine du dépôt introuvable : aucun Directory.Build.props dans les répertoires parents.");
    }
}
