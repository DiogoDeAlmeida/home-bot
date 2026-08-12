using HomelabHub.Abstractions.Platform;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HomelabHub.Infrastructure.Persistence;

/// <summary>
/// Le fichier SQLite : son emplacement, sa migration, et sa copie cohérente.
/// </summary>
/// <remarks>
/// <para>
/// La base vit dans le répertoire de données, donc dans l'archive de sauvegarde et hors du
/// chemin d'une mise à jour (ADR-0007). Un seul fichier, aucune installation, aucun service
/// à administrer : c'est le point entier du choix de SQLite pour un LXC mono-utilisateur.
/// </para>
/// </remarks>
public sealed class HubDatabase(
    IDbContextFactory<HubDbContext> contexts,
    IHubPlatform platform,
    ILogger<HubDatabase> logger)
{
    public const string FileName = "homelabhub.db";

    public string Path => System.IO.Path.Combine(platform.DataDirectory, FileName);

    /// <summary>Existe-t-il déjà une base — c'est-à-dire y a-t-il quelque chose à perdre ?</summary>
    public bool Exists => File.Exists(Path);

    /// <summary>Chaîne de connexion, en écriture, avec création du fichier si besoin.</summary>
    public static string ConnectionStringFor(string path) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // Un seul processus écrit ; le partage entre connexions du même processus est
            // nécessaire pour que le pool fonctionne.
            Cache = SqliteCacheMode.Shared,
        }.ToString();

    /// <summary>Migrations en attente, sans rien appliquer.</summary>
    public IReadOnlyList<string> PendingMigrations()
    {
        using var context = contexts.CreateDbContext();
        return [.. context.Database.GetPendingMigrations()];
    }

    /// <summary>
    /// Applique les migrations et arme le mode WAL.
    /// </summary>
    /// <remarks>
    /// Les migrations sont appliquées par l'application elle-même, jamais par <c>dotnet ef</c> :
    /// le SDK .NET n'est pas installé sur le LXC, et une mise à jour ne doit demander aucune
    /// commande manuelle (ADR-0007). L'échec est fatal — voir <c>Program.cs</c>.
    /// </remarks>
    public void Migrate()
    {
        using var context = contexts.CreateDbContext();
        context.Database.Migrate();

        // WAL : les lectures de l'interface ne bloquent plus l'écriture d'un cycle d'ingestion,
        // et inversement. Le réglage est persistant dans le fichier, mais le réappliquer à
        // chaque démarrage coûte une requête et couvre le cas d'une base restaurée d'ailleurs.
        context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

        logger.LogInformation("Base {Path} migrée et en mode WAL.", Path);
    }

    /// <summary>
    /// Écrit une copie cohérente de la base à l'emplacement donné.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pourquoi pas une simple copie de fichier.</b> En mode WAL, les écritures récentes sont
    /// dans <c>homelabhub.db-wal</c>, pas dans <c>homelabhub.db</c>. Copier le seul fichier
    /// principal pendant que le hub tourne produit une archive qui s'ouvre, se restaure, et a
    /// perdu les dernières minutes — ou pire, une base incohérente si une transaction était en
    /// cours. C'est le genre de défaut qui ne se découvre qu'au moment de la restauration.
    /// </para>
    /// <para>
    /// <c>VACUUM INTO</c> prend un verrou de lecture, écrit une base complète et compactée, et
    /// n'a besoin d'aucune coordination avec les écrivains. C'est la méthode que SQLite
    /// recommande depuis la 3.27 exactement pour cet usage.
    /// </para>
    /// </remarks>
    public void SnapshotTo(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        // VACUUM INTO refuse d'écraser un fichier existant.
        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        using var context = contexts.CreateDbContext();

        // L'argument de INTO est une expression SQL, donc un paramètre lié convient : le chemin
        // ne se concatène jamais dans le texte de la requête.
        context.Database.ExecuteSql($"VACUUM INTO {destination}");
    }
}
