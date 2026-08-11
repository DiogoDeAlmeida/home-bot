using HomelabHub.Abstractions.Dashboard;
using HomelabHub.Abstractions.Modules;
using HomelabHub.Modules.Media.Correlation;

namespace HomelabHub.Modules.Media;

/// <summary>
/// Bloc média du tableau de bord : le palmarès et le résumé chiffré.
/// </summary>
/// <remarks>
/// <para>
/// Le widget expose des données <b>déjà triées et bornées</b>. Ce n'est pas une commodité :
/// c'est ce qui garantit que le message permanent d'un salon et la page web montrent la même
/// sélection. Si chaque adaptateur tronquait la file à sa façon, ils divergeraient sans que
/// personne ne s'en aperçoive avant de les comparer.
/// </para>
/// <para>
/// Les données restent brutes — octets, secondes, énumérations. Le formatage appartient à
/// chaque adaptateur (ADR-0006).
/// </para>
/// </remarks>
internal sealed class MediaWidget(IModuleState<MediaSnapshot> state) : IWidgetProvider
{
    public WidgetDescriptor Descriptor { get; } =
        new("media.overview", "Téléchargements", ShowOnChatDashboard: true, Order: 10);

    public Task<WidgetPayload> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new WidgetPayload(
            Descriptor.Key,
            MediaOverview.From(state.Current),
            DateTimeOffset.UtcNow));
}
