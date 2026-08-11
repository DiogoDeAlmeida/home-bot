using System.Reflection;
using HomelabHub.Core;
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
app.MapModules();
app.MapCapabilities();

await app.RunAsync();

/// <summary>Point d'entrée exposé pour les tests d'intégration.</summary>
public partial class Program;
