using HomelabHub.Abstractions.Modules;

namespace HomelabHub.Core.Modules;

/// <summary>Tout ce que le noyau sait d'un module après son enregistrement.</summary>
public sealed class ModuleDescriptor
{
    internal ModuleDescriptor(IHubModule module, ModuleRegistrationContext context)
    {
        Module = module;
        ModuleType = module.GetType();
        CapabilityTypes = [.. context.CapabilityTypes];
        WidgetTypes = [.. context.WidgetTypes];
        HealthCheckTypes = [.. context.HealthCheckTypes];
        Pollers = [.. context.Pollers];
        Webhooks = [.. context.Webhooks];
        ConnectionTypes = [.. context.ConnectionTypes];
    }

    public IHubModule Module { get; }

    public Type ModuleType { get; }

    public string Key => Module.Key;

    public string DisplayName => Module.DisplayName;

    public IReadOnlyList<Type> CapabilityTypes { get; }

    public IReadOnlyList<Type> WidgetTypes { get; }

    public IReadOnlyList<Type> HealthCheckTypes { get; }

    public IReadOnlyList<PollerRegistration> Pollers { get; }

    public IReadOnlyList<WebhookRegistration> Webhooks { get; }

    public IReadOnlyList<Type> ConnectionTypes { get; }
}

/// <param name="PollerType">Implémentation d'<c>IModulePoller</c>.</param>
/// <param name="DefaultInterval">Cadence retenue si rien n'est configuré.</param>
/// <param name="IntervalConfigKey">Clé relative permettant de surcharger la cadence, ou <c>null</c>.</param>
public sealed record PollerRegistration(Type PollerType, TimeSpan DefaultInterval, string? IntervalConfigKey);

/// <param name="HookName">Segment de route sous <c>/api/webhooks/{module}/</c>.</param>
/// <param name="HandlerType">Implémentation d'<c>IModuleWebhookHandler</c>.</param>
public sealed record WebhookRegistration(string HookName, Type HandlerType);
