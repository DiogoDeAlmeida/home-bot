using System.Reflection;
using HomelabHub.Abstractions.Platform;

namespace HomelabHub.Infrastructure;

/// <summary>Emplacements sur disque, surchargeables par configuration ou variable d'environnement.</summary>
/// <remarks>
/// Les valeurs par défaut suivent le packaging LXC : données persistantes dans
/// <c>/opt/homelabhub/data</c> — jamais écrasé par une mise à jour — et configuration dans
/// <c>/etc/homelabhub</c>. En développement sous Windows, tout retombe sous le répertoire
/// courant pour ne rien éparpiller.
/// </remarks>
public sealed class HubOptions
{
    public const string SectionName = "Hub";

    public string DataDirectory { get; set; } =
        OperatingSystem.IsWindows() ? "./data" : "/opt/homelabhub/data";

    public string ConfigDirectory { get; set; } =
        OperatingSystem.IsWindows() ? "./config" : "/etc/homelabhub";

    /// <summary>Nombre d'archives conservées par la rétention.</summary>
    public int BackupRetention { get; set; } = 10;
}

internal sealed class HubPlatform : IHubPlatform
{
    public HubPlatform(HubOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        DataDirectory = Path.GetFullPath(options.DataDirectory);
        ConfigDirectory = Path.GetFullPath(options.ConfigDirectory);

        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ConfigDirectory);

        Version = Assembly.GetEntryAssembly()
                      ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                      ?.InformationalVersion
                  ?? "0.0.0-inconnue";

        StartedAt = DateTimeOffset.UtcNow;
    }

    public string Version { get; }

    public DateTimeOffset StartedAt { get; }

    public string DataDirectory { get; }

    public string ConfigDirectory { get; }

    /// <summary>Keyring Data Protection. Sauvegardé avec la base, jamais séparément (ADR-0007).</summary>
    public string KeysDirectory => Path.Combine(DataDirectory, "keys");

    public string BackupsDirectory => Path.Combine(DataDirectory, "backups");

    public string ConfigFilePath => Path.Combine(ConfigDirectory, "hub.json");
}
