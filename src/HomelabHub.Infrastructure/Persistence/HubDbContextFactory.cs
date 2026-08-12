using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HomelabHub.Infrastructure.Persistence;

/// <summary>
/// Contexte utilisé <b>uniquement</b> par l'outil de migration, au moment du développement.
/// </summary>
/// <remarks>
/// <c>dotnet ef</c> doit pouvoir construire un contexte sans démarrer l'application. Sans cette
/// fabrique, il tenterait de monter le Host entier — avec son verrou de premier démarrage, sa
/// protection des données et ses modules — pour produire un fichier de migration.
/// <para>
/// La base pointée ici n'est jamais celle de production : elle sert uniquement à calculer le
/// différentiel de schéma.
/// </para>
/// </remarks>
internal sealed class HubDbContextFactory : IDesignTimeDbContextFactory<HubDbContext>
{
    public HubDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;

        return new HubDbContext(options);
    }
}
