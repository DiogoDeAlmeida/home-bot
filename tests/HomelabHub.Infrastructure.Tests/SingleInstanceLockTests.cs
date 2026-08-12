using Xunit;

namespace HomelabHub.Infrastructure.Tests;

/// <summary>
/// Le verrou qui empêche deux processus du hub de travailler sur le même répertoire de
/// données à la fois.
/// </summary>
/// <remarks>
/// Écrit après un incident réel : un message de tableau de bord Discord s'était dédoublé parce
/// que deux instances du hub — l'une lancée pour tester, l'autre une session de débogage VS
/// Code oubliée — tournaient en même temps sur le même répertoire. Aucun des deux processus
/// n'avait le moindre moyen de le savoir.
/// </remarks>
public sealed class SingleInstanceLockTests
{
    [Fact]
    public void Une_premiere_instance_prend_le_verrou_sans_probleme()
    {
        var directory = NewTempDirectory();

        using var first = SingleInstanceLock.Acquire(directory);

        Assert.True(File.Exists(Path.Combine(directory, "hub.lock")));
    }

    [Fact]
    public void Une_seconde_instance_est_refusee_tant_que_la_premiere_tient_le_verrou()
    {
        var directory = NewTempDirectory();
        using var first = SingleInstanceLock.Acquire(directory);

        var ex = Assert.Throws<SingleInstanceLockException>(() => SingleInstanceLock.Acquire(directory));

        // Le message est ce qu'un exploitant lit dans le journal systemd : il doit pointer vers
        // une cause plausible, pas seulement dire que ça a échoué.
        Assert.Contains("Une autre instance", ex.Message, StringComparison.Ordinal);
        Assert.Contains(directory, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Liberer_la_premiere_instance_permet_a_une_seconde_de_demarrer()
    {
        // C'est ce qui distingue ce verrou d'un fichier PID : la libération est automatique,
        // pas une ligne de nettoyage que le processus pourrait sauter en cas de crash.
        var directory = NewTempDirectory();

        var first = SingleInstanceLock.Acquire(directory);
        first.Dispose();

        using var second = SingleInstanceLock.Acquire(directory);
    }

    [Fact]
    public void Le_repertoire_est_cree_sil_nexiste_pas_encore()
    {
        var directory = Path.Combine(Path.GetTempPath(), "homelabhub-lock-tests",
                                     Guid.NewGuid().ToString("N"), "data");

        using var first = SingleInstanceLock.Acquire(directory);

        Assert.True(Directory.Exists(directory));
    }

    private static string NewTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "homelabhub-lock-tests",
                                     Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
