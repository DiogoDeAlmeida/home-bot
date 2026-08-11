using HomelabHub.Abstractions.Dashboard;
using HomelabHub.Abstractions.Modules;

namespace HomelabHub.Modules.SystemInfo;

/// <summary>
/// Bloc de synthèse du hub pour le tableau de bord.
/// </summary>
/// <remarks>
/// Données pures, aucune présentation (ADR-0006) : c'est l'adaptateur web ou Discord qui décide
/// comment rendre ça. Et la lecture se fait sur le snapshot, jamais en interrogeant le système
/// de fichiers — un tableau de bord se rafraîchit souvent.
/// </remarks>
internal sealed class SystemOverviewWidget(IModuleState<SystemSnapshot> state) : IWidgetProvider
{
    public WidgetDescriptor Descriptor { get; } =
        new("system.overview", "Hub", ShowOnDiscordDashboard: true, Order: 100);

    public Task<WidgetPayload> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new WidgetPayload(Descriptor.Key, state.Current, DateTimeOffset.UtcNow));
}
