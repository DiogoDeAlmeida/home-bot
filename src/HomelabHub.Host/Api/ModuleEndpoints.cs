using HomelabHub.Abstractions.Configuration;
using HomelabHub.Abstractions.Modules;
using HomelabHub.Core.Configuration;
using HomelabHub.Core.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace HomelabHub.Host.Api;

internal static class ModuleEndpoints
{
    public static void MapModules(this IEndpointRouteBuilder app)
    {
        var modules = app.MapGroup("/api/modules").RequireAuthorization();

        modules.MapGet("/", (IModuleRegistry registry) => Results.Ok(
            registry.Modules.Select(module =>
            {
                var activation = registry.GetActivation(module.Key);
                return new
                {
                    key = module.Key,
                    displayName = module.DisplayName,
                    description = module.Module.Description,
                    activation.Enabled,
                    activation.ConfigurationComplete,
                    activation.IsActive,
                    activation.BlockedReason,
                    capabilities = module.CapabilityTypes.Count,
                    pollers = module.Pollers.Count,
                    webhooks = module.Webhooks.Select(w => w.HookName),
                };
            })));

        modules.MapPost("/{key}/enabled", async (string key, SetEnabledRequest request,
                                                 IModuleRegistry registry,
                                                 CancellationToken cancellationToken) =>
        {
            if (registry.Modules.All(m => !string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase)))
            {
                return Results.NotFound();
            }

            await registry.SetEnabledAsync(key, request.Enabled, cancellationToken).ConfigureAwait(false);
            return Results.Ok(registry.GetActivation(key));
        });

        modules.MapGet("/{key}/health", async (string key, ModuleCatalog catalog,
                                               IModuleRegistry registry, IServiceProvider services,
                                               CancellationToken cancellationToken) =>
        {
            var descriptor = catalog.Find(key);
            if (descriptor is null)
            {
                return Results.NotFound();
            }

            if (!registry.IsActive(key))
            {
                return Results.Ok(ModuleHealth.Disabled(DateTimeOffset.UtcNow));
            }

            var checks = descriptor.HealthCheckTypes
                .Select(type => (IModuleHealthCheck)services.GetRequiredService(type))
                .ToArray();

            if (checks.Length == 0)
            {
                return Results.Ok(new ModuleHealth(HealthState.Unknown,
                    "Ce module ne déclare pas de sonde de santé.", [], DateTimeOffset.UtcNow));
            }

            var results = await Task.WhenAll(checks.Select(c => c.CheckAsync(cancellationToken)))
                                    .ConfigureAwait(false);

            return Results.Ok(results.Length == 1 ? results[0] : Merge(results));
        });

        // ── Configuration : schéma + valeurs, secrets masqués ───────────────────────────
        modules.MapGet("/{key}/config", (string key, ModuleCatalog catalog, IHubConfigStore store) =>
        {
            var descriptor = catalog.Find(key);
            if (descriptor is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new
            {
                key = descriptor.Key,
                fields = descriptor.Module.ConfigSchema.Fields.Select(declared => new
                {
                    declared.Key,
                    declared.Label,
                    kind = declared.Kind.ToString(),
                    declared.Required,
                    declared.Secret,
                    declared.Help,
                    declared.DefaultValue,
                    declared.Options,
                    // Présent dans le contrat, non résolu en v1 : le front rend une saisie
                    // libre tant que personne n'en a réellement besoin (ADR-0011).
                    declared.OptionsFrom,
                    declared.DependsOn,
                    value = ReadForDisplay(store, descriptor.Key, declared),
                }),
            });
        });

        modules.MapPut("/{key}/config", async (string key, Dictionary<string, string?> values,
                                               ModuleCatalog catalog, IHubConfigStore store,
                                               CancellationToken cancellationToken) =>
        {
            var descriptor = catalog.Find(key);
            if (descriptor is null)
            {
                return Results.NotFound();
            }

            var schema = descriptor.Module.ConfigSchema.Fields
                .ToDictionary(declared => declared.Key, StringComparer.OrdinalIgnoreCase);

            var writes = new Dictionary<string, ConfigValue>(StringComparer.OrdinalIgnoreCase);
            var rejected = new List<string>();

            foreach (var (field, value) in values)
            {
                if (!schema.TryGetValue(field, out var declared))
                {
                    // Une clé hors schéma est refusée : le formulaire est généré depuis le
                    // schéma, donc une clé inconnue est soit une faute de frappe, soit un abus.
                    rejected.Add(field);
                    continue;
                }

                // Un secret réaffiché masqué et renvoyé tel quel ne doit pas écraser la vraie
                // valeur : c'est le piège classique du formulaire en écriture seule.
                if (declared.Secret && value is not null && value.All(c => c == '•'))
                {
                    continue;
                }

                writes[$"{descriptor.Key}.{declared.Key}"] = new ConfigValue(value, declared.Secret);
            }

            if (rejected.Count > 0)
            {
                return Results.BadRequest(new { error = "unknown_fields", fields = rejected });
            }

            await store.SetManyAsync(writes, cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        });
    }

    /// <summary>
    /// Un secret ne repart jamais en clair de l'API : écriture seule, lecture masquée.
    /// </summary>
    private static string? ReadForDisplay(IHubConfigStore store, string moduleKey, ConfigField declared)
    {
        var value = store.GetValue($"{moduleKey}.{declared.Key}");

        if (!declared.Secret || string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= 4
            ? new string('•', 8)
            : new string('•', 6) + value[^4..];
    }

    private static ModuleHealth Merge(IReadOnlyList<ModuleHealth> results) => new(
        results.Max(r => r.State),
        string.Join(" ", results.Select(r => r.Message).Where(m => !string.IsNullOrWhiteSpace(m))),
        [.. results.SelectMany(r => r.Services)],
        DateTimeOffset.UtcNow);

    internal sealed record SetEnabledRequest(bool Enabled);
}
