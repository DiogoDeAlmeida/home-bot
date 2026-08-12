using System.Reflection;
using HomelabHub.Abstractions.Platform;
using HomelabHub.Core;
using HomelabHub.Core.Backup;
using HomelabHub.Core.Configuration;
using HomelabHub.Core.Modules;
using HomelabHub.Discord;
using HomelabHub.Host.Api;
using HomelabHub.Host.Auth;
using HomelabHub.Infrastructure;
using HomelabHub.Infrastructure.Persistence;
using HomelabHub.Modules.Media;
using HomelabHub.Modules.SystemInfo;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

// ─────────────────────────────────────────────────────────────────────────────────────
//  Homelab Hub — racine de composition.
//
//  Un seul processus : API web, adaptateur Discord (étape 3) et tâches d'ingestion.
//  Un binaire, une unité systemd, un packaging LXC simple.
// ─────────────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// Niveau de journalisation modifiable depuis l'interface, sans redémarrage ni SSH. Le délégué
// est évalué à chaque appel de log : changer la propriété suffit à changer le comportement.
//
// SetMinimumLevel(Trace) ouvre la porte au maximum et laisse ce délégué décider seul. Sans
// cela, une règle issue d'appsettings l'emporterait en silence : le réglage de l'interface
// serait accepté et sans effet, ce qui est le pire des deux mondes.
var logLevel = new LogLevelSwitch();
builder.Logging.SetMinimumLevel(LogLevel.Trace);
builder.Logging.AddFilter(logLevel.IsEnabled);
builder.Services.AddSingleton(logLevel);

// Le verrou de première instance (SingleInstanceLock) est pris dès la première ligne de cet
// appel, avant tout accès au keyring, à la configuration ou à la base. La journalisation
// structurée n'existe pas encore à ce stade — Build() n'a pas eu lieu — d'où stderr brut plutôt
// que ILogger : systemd capture l'un comme l'autre.
try
{
    builder.Services.AddHubInfrastructure(builder.Configuration);
}
catch (SingleInstanceLockException ex)
{
    await Console.Error.WriteLineAsync(ex.Message);
    return 1;
}

// Tous les modules sont enregistrés, activés ou non : le conteneur est immuable après
// Build(), l'activation est un état runtime (ADR-0002). Le module média est déclaré avant
// d'être fonctionnel : il expose son schéma, donc son formulaire, et se signale lui-même
// comme inactif tant que ses clés d'API ne sont pas saisies.
builder.Services.AddHubCore(new SystemModule(), new MediaModule());
builder.Services.AddDiscordAdapter();

builder.Services.AddSingleton<AdminAccount>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "homelabhub.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // Le hub reste sur le LAN et derrière le WireGuard existant : imposer Secure
        // interdirait l'accès en HTTP simple, qui est le mode nominal ici.
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;

        // API : répondre 401/403 plutôt que rediriger vers une page de connexion inexistante.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Échoue ici, au démarrage, plutôt qu'au premier appel : une capacité mal déclarée doit se
// voir au lancement.
app.Services.ValidateHubDeclarations();

// Le niveau stocké prend effet avant même le premier journal applicatif.
logLevel.ApplyFrom(app.Services.GetRequiredService<IHubConfigStore>());

// ─── Séquence de démarrage de la base (ADR-0007) ─────────────────────────────────────
//
//  sauvegarde → migration → hydratation. Dans cet ordre, et fatale à la première erreur.
//
//  Le principe : une migration qui échoue à mi-chemin laisse un schéma que personne n'a
//  jamais testé. Continuer à démarrer là-dessus donnerait un hub qui tourne, notifie, et
//  écrit dans une base à moitié transformée — un dégât silencieux. Refuser de démarrer est
//  bruyant, immédiat, et l'archive prise juste avant permet de revenir en arrière.
await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<HubDatabase>();
    var startup = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    try
    {
        var pending = database.PendingMigrations();

        // Sauvegarder uniquement s'il y a quelque chose à perdre ET quelque chose à changer.
        // Une archive à chaque démarrage ferait tourner la rétention à vide et chasserait les
        // archives qui comptent — celles d'avant les migrations.
        if (pending.Count > 0 && database.Exists)
        {
            startup.LogInformation("{Count} migration(s) en attente : sauvegarde préalable.",
                pending.Count);

            var archive = await scope.ServiceProvider.GetRequiredService<IHubBackupService>()
                .CreateAsync($"avant migration ({string.Join(", ", pending)})",
                             CancellationToken.None);

            startup.LogInformation("Sauvegarde de sécurité : {File}.", archive.FileName);
        }

        database.Migrate();
    }
    catch (Exception ex)
    {
        startup.LogCritical(ex,
            "Migration de la base impossible. Le hub refuse de démarrer sur un schéma incertain. " +
            "La dernière archive de {Directory} contient l'état d'avant la tentative.",
            scope.ServiceProvider.GetRequiredService<IHubPlatform>().DataDirectory);

        return 1;
    }
}

// La table d'anomalies est rechargée avant le premier cycle d'ingestion : une anomalie
// toujours présente ne doit pas être vue comme nouvelle, donc renotifiée, à chaque
// redémarrage. C'est le bénéfice entier de la persistance.
app.Services.HydrateHubState();

// L'interface React, buildée dans wwwroot par Vite, est servie en statique par ce même
// processus : une seule origine, donc aucun CORS et aucun proxy inverse à configurer.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseMiddleware<SetupGateMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

var version = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? "inconnue";

// Sonde consommée par systemd et par le script de mise à jour, qui doit vérifier qu'un
// redémarrage a réellement abouti avant de déclarer la migration réussie ou de la faire
// suivre d'un rollback (ADR-0007, ADR-0019).
//
// Avant cette sonde, /healthz ne vérifiait rien d'autre que « le processus répond aux requêtes
// HTTP » — un hub dont la connexion Discord échouait en silence répondait quand même 200. Trois
// vérifications réelles, chacune pouvant à elle seule faire échouer la sonde :
//
//  - base : lisible, et sans migration en attente — un schéma à moitié appliqué doit se voir ici,
//    pas seulement au prochain redémarrage manqué ;
//  - Discord : Connected ou NotConfigured comptent comme sains ; Connecting compte comme
//    dégradé — un redémarrage tout juste terminé passe par cet état le temps de la poignée de
//    main, d'où la fenêtre de tolérance côté script de mise à jour plutôt qu'ici ;
//  - modules : le module système (aucune configuration requise) doit être actif — son
//    inactivité ne peut venir que d'une régression du noyau de modules lui-même, jamais d'une clé
//    d'API absente.
app.MapGet("/healthz", async (
    IDbContextFactory<HubDbContext> contexts,
    IModuleRegistry moduleRegistry,
    IDiscordConnectionStatus discordStatus,
    CancellationToken cancellationToken) =>
{
    var healthy = true;
    string database;

    try
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        var canConnect = await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
        var pending = await context.Database.GetPendingMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);

        database = canConnect && !pending.Any() ? "ok" : "migration en attente";
        healthy &= canConnect && !pending.Any();
    }
    catch (Exception ex)
    {
        database = $"injoignable : {ex.Message}";
        healthy = false;
    }

    var discordHealthy = discordStatus.State
        is DiscordConnectionState.Connected or DiscordConnectionState.NotConfigured;
    healthy &= discordHealthy;
    var discord = discordStatus.State switch
    {
        DiscordConnectionState.NotConfigured => "non configuré",
        DiscordConnectionState.Connecting => "connexion en cours",
        DiscordConnectionState.Connected => "connecté",
        DiscordConnectionState.Failed => $"échec : {discordStatus.Detail}",
        _ => "inconnu",
    };

    var systemModuleActive = moduleRegistry.IsActive("system");
    healthy &= systemModuleActive;
    var modules = systemModuleActive ? "ok" : "module système inactif";

    var body = new
    {
        status = healthy ? "ok" : "unhealthy",
        version,
        checks = new { database, discord, modules },
    };

    return healthy
        ? Results.Ok(body)
        : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();

app.MapSetupAndAuth();
app.MapHub();
app.MapModules();
app.MapCapabilities();
app.MapAnomalies();

// Une route d'API inexistante doit répondre 404 en JSON, pas renvoyer la page React : sinon un
// appel mal orthographié réussit avec du HTML et le bug se cherche longtemps.
app.Map("/api/{**rest}", () => Results.NotFound(new { error = "unknown_endpoint" }))
   .AllowAnonymous();

// Toute autre URL rend l'interface : le routage des pages se fait côté client.
app.MapFallbackToFile("index.html").AllowAnonymous();

await app.RunAsync();

// Le code de sortie est lu par systemd : 0 pour un arrêt normal, 1 quand la migration a été
// refusée plus haut. Un Restart=on-failure distingue ainsi une mise à jour cassée d'un arrêt
// demandé, et cesse de relancer un binaire qui ne peut pas démarrer.
return 0;

/// <summary>Point d'entrée exposé pour les tests d'intégration.</summary>
public partial class Program;
