using System.Text.RegularExpressions;
using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Abstractions.Dashboard;
using HomelabHub.Abstractions.Ingest;
using HomelabHub.Abstractions.Modules;
using HomelabHub.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomelabHub.Core.Modules;

/// <summary>
/// Implémentation du contexte remis à un module pendant <see cref="IHubModule.Register"/>.
/// </summary>
/// <remarks>
/// Chaque méthode fait deux choses : enregistrer le type dans le conteneur, et noter sa
/// présence pour que le noyau sache quoi démarrer, exposer ou router. Les modules désactivés
/// passent quand même par ici — le conteneur est immuable après <c>Build()</c>, l'activation
/// est un état runtime (ADR-0002).
/// </remarks>
internal sealed partial class ModuleRegistrationContext(string moduleKey, IServiceCollection services)
    : IModuleRegistrationContext
{
    public string ModuleKey { get; } = moduleKey;

    public IServiceCollection Services { get; } = services;

    internal List<Type> CapabilityTypes { get; } = [];

    internal List<Type> WidgetTypes { get; } = [];

    internal List<Type> HealthCheckTypes { get; } = [];

    internal List<Type> ConnectionTypes { get; } = [];

    internal List<PollerRegistration> Pollers { get; } = [];

    internal List<WebhookRegistration> Webhooks { get; } = [];

    public IModuleRegistrationContext AddState<TSnapshot>(TSnapshot initial)
        where TSnapshot : class
    {
        ArgumentNullException.ThrowIfNull(initial);

        // Le type de snapshot sert de clé : deux modules ne peuvent pas partager le même,
        // ce qui est le comportement voulu — un snapshot appartient à un module.
        Services.AddSingleton<IModuleState<TSnapshot>>(sp =>
            new ModuleState<TSnapshot>(initial,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ModuleState<TSnapshot>>>()));

        return this;
    }

    public IModuleRegistrationContext AddPoller<T>(TimeSpan defaultInterval,
                                                   string? intervalConfigKey = null)
        where T : class, IModulePoller
    {
        if (defaultInterval <= TimeSpan.Zero)
        {
            throw new HubConfigurationException(
                $"Module « {ModuleKey} » : l'intervalle de {typeof(T).Name} doit être strictement positif.");
        }

        Services.AddSingleton<T>();
        Pollers.Add(new PollerRegistration(typeof(T), defaultInterval, intervalConfigKey));
        return this;
    }

    public IModuleRegistrationContext AddWebhook<T>(string hookName)
        where T : class, IModuleWebhookHandler
    {
        if (!HookNamePattern().IsMatch(hookName))
        {
            throw new HubConfigurationException(
                $"Module « {ModuleKey} » : nom de webhook « {hookName} » invalide. " +
                "Attendu : minuscules, chiffres et tirets, 1 à 32 caractères.");
        }

        if (Webhooks.Exists(w => w.HookName == hookName))
        {
            throw new HubConfigurationException(
                $"Module « {ModuleKey} » : webhook « {hookName} » déclaré deux fois.");
        }

        Services.AddSingleton<T>();
        Webhooks.Add(new WebhookRegistration(hookName, typeof(T)));
        return this;
    }

    public IModuleRegistrationContext AddConnection<T>()
        where T : class, IModuleConnection
    {
        Services.AddSingleton<T>();
        ConnectionTypes.Add(typeof(T));
        return this;
    }

    public IModuleRegistrationContext AddCapability<T>() where T : class, IHubCapability
    {
        Services.AddSingleton<T>();
        CapabilityTypes.Add(typeof(T));
        return this;
    }

    public IModuleRegistrationContext AddWidget<T>() where T : class, IWidgetProvider
    {
        Services.AddSingleton<T>();
        WidgetTypes.Add(typeof(T));
        return this;
    }

    public IModuleRegistrationContext AddHealthCheck<T>() where T : class, IModuleHealthCheck
    {
        Services.AddSingleton<T>();
        HealthCheckTypes.Add(typeof(T));
        return this;
    }

    public IHttpClientBuilder AddServiceClient<TClient, TImpl>(string configKeyPrefix)
        where TClient : class
        where TImpl : class, TClient
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configKeyPrefix);

        var key = ModuleKey;

        return Services.AddHttpClient<TClient, TImpl>((sp, client) =>
        {
            var config = sp.GetRequiredService<IHubConfigStore>();

            var baseUrl = config.GetValue($"{key}.{configKeyPrefix}.url");
            if (!string.IsNullOrWhiteSpace(baseUrl)
                && Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            {
                client.BaseAddress = uri;
            }

            var apiKey = config.GetValue($"{key}.{configKeyPrefix}.apiKey");
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", apiKey);
            }

            // Convention §14 : jamais d'appel sortant sans délai d'attente explicite.
            client.Timeout = TimeSpan.FromSeconds(
                config.GetInt32($"{key}.{configKeyPrefix}.timeoutSeconds", 15));
        });

        // Réessais, disjoncteur et journalisation expurgée arrivent avec le module média
        // (étape 2), quand il y aura de vrais services distants à malmener.
    }

    [GeneratedRegex("^[a-z0-9-]{1,32}$")]
    private static partial Regex HookNamePattern();
}
