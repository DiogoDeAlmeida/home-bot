namespace HomelabHub.Infrastructure;

/// <summary>
/// Empêche deux processus du hub de travailler sur le même répertoire de données à la fois.
/// </summary>
/// <remarks>
/// <para>
/// <b>Trouvé en production, pas en théorie.</b> Un message de tableau de bord Discord s'est
/// dédoublé sans qu'aucun bug d'édition n'en soit la cause : deux instances du hub tournaient en
/// même temps sur le même <c>.local</c>, chacune ignorant l'existence de l'autre, chacune
/// éditant sa propre idée de « le » message. En creusant, six processus <c>dotnet</c> actifs sur
/// la machine de développement — des sessions de débogage VS Code (Shift+F5) jamais fermées,
/// accumulées au fil des tranches précédentes. Le tableau de bord dupliqué n'était que le
/// symptôme visible ; la config JSON, le keyring et la base SQLite auraient pu tout aussi bien
/// diverger en silence, sans qu'aucun message d'erreur ne le signale jamais.
/// </para>
/// <para>
/// <b>Un handle de fichier exclusif, pas un fichier PID.</b> Un PID mémorisé sur disque peut
/// survivre au processus qui l'a écrit — un crash, un <c>kill -9</c> — et bloquerait alors un
/// redémarrage parfaitement légitime tant que personne ne l'efface à la main. Un
/// <see cref="FileShare.None"/> tenu par le système d'exploitation n'a pas ce problème : il est
/// libéré par l'OS à l'instant où le processus qui le tient se termine, quelle que soit la
/// façon dont il se termine. .NET traduit ce mode de partage en <c>flock()</c> sur Linux depuis
/// .NET Core 2.1, donc le même code protège aussi bien le LXC de production que ce poste de
/// développement.
/// </para>
/// <para>
/// <b>Pris avant tout accès au répertoire de données</b> — keyring, configuration JSON, base
/// SQLite — pour qu'aucune des deux instances ne puisse écrire quoi que ce soit en concurrence.
/// Voir <see cref="HubInfrastructureServiceCollectionExtensions.AddHubInfrastructure"/>, premier
/// appel après la construction de <c>HubPlatform</c>.
/// </para>
/// </remarks>
public sealed class SingleInstanceLock : IDisposable
{
    /// <summary>
    /// Nom du fichier de verrou, dans le répertoire de données.
    /// </summary>
    /// <remarks>
    /// Exposé pour que <see cref="Backup.BackupService"/> puisse l'exclure de l'archive : ce
    /// fichier ne porte aucune donnée à préserver, seulement la détection d'un processus
    /// vivant — et il est justement tenu ouvert en exclusivité par le processus qui archive,
    /// ce qui ferait échouer la sauvegarde si on tentait de le lire.
    /// </remarks>
    internal const string FileName = "hub.lock";

    private readonly FileStream _handle;

    private SingleInstanceLock(FileStream handle) => _handle = handle;

    /// <summary>
    /// Prend le verrou, ou lève <see cref="SingleInstanceLockException"/> avec un message
    /// explicite si une autre instance le détient déjà.
    /// </summary>
    public static SingleInstanceLock Acquire(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, FileName);

        FileStream handle;
        try
        {
            handle = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            throw new SingleInstanceLockException(
                $"""
                 Une autre instance du hub tient déjà le verrou {path}.

                 Ce processus refuse de démarrer plutôt que de risquer une écriture concurrente
                 sur la configuration, le keyring ou la base. Vérifier :
                   - qu'aucune session de débogage (VS Code, Shift+F5) n'est restée ouverte sur
                     ce projet — c'est la cause la plus fréquente ;
                   - qu'aucun processus dotnet orphelin ne pointe encore sur {dataDirectory}
                     (Gestionnaire des tâches, ou « pgrep -f homelabhub » sur le LXC).

                 Fermer l'instance en trop, puis relancer.
                 """, ex);
        }

        // Le contenu n'est lu par aucun code — seule l'existence du handle exclusif protège
        // quoi que ce soit. Il est renseigné tout de même : utile en diagnostic si quelqu'un
        // l'ouvre à la main pendant une investigation, comme celle qui a motivé ce fichier.
        handle.SetLength(0);
        using (var writer = new StreamWriter(handle, leaveOpen: true))
        {
            writer.Write($"pid={Environment.ProcessId} started={DateTimeOffset.UtcNow:O}");
            writer.Flush();
        }

        return new SingleInstanceLock(handle);
    }

    public void Dispose() => _handle.Dispose();
}

/// <summary>Levée quand le verrou de première instance est déjà tenu par un autre processus.</summary>
public sealed class SingleInstanceLockException : Exception
{
    public SingleInstanceLockException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SingleInstanceLockException()
    {
    }

    public SingleInstanceLockException(string message) : base(message)
    {
    }
}
