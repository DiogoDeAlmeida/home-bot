using System.Reflection;
using HomelabHub.Core;
using HomelabHub.Core.Configuration;
using HomelabHub.Host.Api;
using HomelabHub.Host.Auth;
using HomelabHub.Infrastructure;
using HomelabHub.Modules.SystemInfo;
using Microsoft.AspNetCore.Authentication.Cookies;

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

builder.Services.AddHubInfrastructure(builder.Configuration);

// Tous les modules sont enregistrés, activés ou non : le conteneur est immuable après
// Build(), l'activation est un état runtime (ADR-0002).
builder.Services.AddHubCore(new SystemModule());

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
// redémarrage a réellement abouti avant de déclarer la migration réussie (ADR-0007).
app.MapGet("/healthz", () => Results.Ok(new { status = "ok", version })).AllowAnonymous();

app.MapSetupAndAuth();
app.MapHub();
app.MapModules();
app.MapCapabilities();

// Une route d'API inexistante doit répondre 404 en JSON, pas renvoyer la page React : sinon un
// appel mal orthographié réussit avec du HTML et le bug se cherche longtemps.
app.Map("/api/{**rest}", () => Results.NotFound(new { error = "unknown_endpoint" }))
   .AllowAnonymous();

// Toute autre URL rend l'interface : le routage des pages se fait côté client.
app.MapFallbackToFile("index.html").AllowAnonymous();

await app.RunAsync();

/// <summary>Point d'entrée exposé pour les tests d'intégration.</summary>
public partial class Program;
