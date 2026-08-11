using HomelabHub.Abstractions.Dashboard;
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

        // Les widgets des modules actifs, agrégés. Données pures : c'est chaque adaptateur qui
        // décide du rendu, il n'y a pas de modèle de présentation partagé (ADR-0006).
        app.MapGet("/api/widgets", async (ModuleCatalog catalog, IModuleRegistry registry,
                                          IServiceProvider services, CancellationToken cancellationToken) =>
        {
            var payloads = new List<(int Order, object Widget)>();

            foreach (var module in catalog.Descriptors.Where(m => registry.IsActive(m.Key)))
            {
                foreach (var type in module.WidgetTypes)
                {
                    var widget = (IWidgetProvider)services.GetRequiredService(type);
                    var descriptor = widget.Descriptor;

                    try
                    {
                        var payload = await widget.GetAsync(cancellationToken).ConfigureAwait(false);

                        payloads.Add((descriptor.Order, new
                        {
                            moduleKey = module.Key,
                            descriptor.Key,
                            descriptor.Title,
                            descriptor.ShowOnChatDashboard,
                            descriptor.Order,
                            payload.Data,
                            payload.GeneratedAt,
                        }));
                    }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        // Convention §14 : un widget en panne laisse un trou dans le tableau de
                        // bord, il ne fait pas tomber la page entière.
                    }
                }
            }

            return Results.Ok(payloads.OrderBy(p => p.Order).Select(p => p.Widget));
        }).RequireAuthorization();

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

        // Configuration : même projection et même écriture que les réglages du hub, au préfixe
        // près. Un seul générateur de formulaire côté React en découle (ADR-0013).
        modules.MapGet("/{key}/config", (string key, ModuleCatalog catalog, IHubConfigStore store) =>
        {
            var descriptor = catalog.Find(key);

            return descriptor is null
                ? Results.NotFound()
                : Results.Ok(ConfigSurface.Describe(descriptor.Key,
                                                    descriptor.Module.ConfigSchema.Fields, store));
        });

        modules.MapPut("/{key}/config", async (string key, Dictionary<string, string?> values,
                                               ModuleCatalog catalog, IHubConfigStore store,
                                               CancellationToken cancellationToken) =>
        {
            var descriptor = catalog.Find(key);

            return descriptor is null
                ? Results.NotFound()
                : await ConfigSurface.WriteAsync(descriptor.Key, descriptor.Module.ConfigSchema.Fields,
                                                 values, store, cancellationToken).ConfigureAwait(false);
        });
    }

    private static ModuleHealth Merge(IReadOnlyList<ModuleHealth> results) => new(
        results.Max(r => r.State),
        string.Join(" ", results.Select(r => r.Message).Where(m => !string.IsNullOrWhiteSpace(m))),
        [.. results.SelectMany(r => r.Services)],
        DateTimeOffset.UtcNow);

    internal sealed record SetEnabledRequest(bool Enabled);
}
