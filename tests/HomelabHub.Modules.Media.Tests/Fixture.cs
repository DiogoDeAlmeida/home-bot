using System.Reflection;
using System.Text.Json;
using HomelabHub.Modules.Media.Contracts;

namespace HomelabHub.Modules.Media.Tests;

/// <summary>
/// Accès aux réponses capturées sur les instances réelles.
/// </summary>
/// <remarks>
/// Les tests désérialisent les <b>vraies</b> réponses de Radarr 6.3, Sonarr 4.0.19, Seerr 3.4.1
/// et qBittorrent 5.1. Un modèle qui cesserait de correspondre à ce que les services renvoient
/// casse ici, et pas en production six mois plus tard.
/// </remarks>
internal static class Fixture
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private static string Root { get; } = LocateFixtures();

    public static T Load<T>(string relativePath)
    {
        var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Fixture introuvable : {path}");
        }

        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
               ?? throw new InvalidOperationException($"Fixture vide : {relativePath}");
    }

    /// <summary>File Radarr ou Sonarr, telle que le client la renvoie après dépagination.</summary>
    public static IReadOnlyList<ArrQueueRecord> Queue(string relativePath) =>
        Load<ArrPage<ArrQueueRecord>>(relativePath).Records;

    public static IReadOnlyList<QBittorrentTorrent> Torrents(string relativePath) =>
        Load<List<QBittorrentTorrent>>(relativePath);

    public static IReadOnlyList<SeerrRequest> Requests(string relativePath) =>
        Load<SeerrPage<SeerrRequest>>(relativePath).Results;

    private static string LocateFixtures()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(
            Assembly.GetExecutingAssembly().Location)!);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Fixtures");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Répertoire Fixtures introuvable.");
    }
}
