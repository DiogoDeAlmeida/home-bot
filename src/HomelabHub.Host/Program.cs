using System.Reflection;

// ─────────────────────────────────────────────────────────────────────────────────────
//  Homelab Hub — racine de composition.
//
//  Étape 0 : squelette. Le processus démarre, expose /healthz, et rien d'autre. Cela
//  suffit à valider la chaîne complète — build, publication, unité systemd, script LXC —
//  avant d'y greffer quoi que ce soit de fonctionnel.
//
//  Étape 1 y ajoutera, dans cet ordre : journalisation structurée, base SQLite et
//  migrations au démarrage (précédées d'une sauvegarde, ADR-0007), magasin de
//  configuration chiffré, découverte et enregistrement des modules, verrou de premier
//  démarrage, authentification par cookie, puis le module système comme banc de test.
// ─────────────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

var version = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? "inconnue";

// Sonde de disponibilité, consommée par systemd et par le script de mise à jour pour
// vérifier qu'un redémarrage a réellement abouti avant de déclarer la migration réussie.
app.MapGet("/healthz", () => Results.Ok(new
{
    status = "ok",
    version,
    startedAt = DateTimeOffset.UtcNow,
}));

await app.RunAsync();
