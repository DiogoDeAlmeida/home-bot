namespace HomelabHub.Abstractions.Dashboard;

/// <summary>
/// Fournit un bloc de données pour le tableau de bord.
/// </summary>
/// <remarks>
/// <b>Données, pas présentation (ADR-0006).</b> Un embed Discord et un composant React n'ont
/// aucune primitive de mise en page commune ; chercher un modèle de rendu partagé mènerait
/// soit à un dashboard web indigent, soit à un moteur de layout maison à maintenir. Un widget
/// expose donc un objet typé et sérialisable, que chaque adaptateur rend à sa façon. Le rendu
/// Discord d'un widget représente quelques dizaines de lignes par module, et c'est une
/// duplication assumée.
/// </remarks>
public interface IWidgetProvider
{
    /// <summary>Description statique du widget.</summary>
    WidgetDescriptor Descriptor { get; }

    /// <summary>
    /// Produit les données. Doit lire le snapshot du module plutôt qu'interroger le réseau :
    /// le tableau de bord se rafraîchit souvent, les services externes ne doivent pas en pâtir.
    /// </summary>
    Task<WidgetPayload> GetAsync(CancellationToken cancellationToken);
}

/// <summary>Description statique d'un widget.</summary>
/// <param name="Key">Identifiant préfixé par le module : <c>media.queue</c>.</param>
/// <param name="Title">Titre affiché, en français.</param>
/// <param name="ShowOnDiscordDashboard">
/// Inclut le widget dans le message permanent Discord. À réserver à l'essentiel : ce message
/// est édité en place et doit rester lisible sur mobile.
/// </param>
/// <param name="Order">Ordre d'affichage, croissant.</param>
public sealed record WidgetDescriptor(
    string Key,
    string Title,
    bool ShowOnDiscordDashboard = false,
    int Order = 0);

/// <summary>Données d'un widget à un instant donné.</summary>
/// <param name="WidgetKey">Widget concerné.</param>
/// <param name="Data">Objet typé, sérialisable, sans information de mise en page.</param>
/// <param name="GeneratedAt">Horodatage, affiché comme « mis à jour il y a N secondes ».</param>
public sealed record WidgetPayload(
    string WidgetKey,
    object Data,
    DateTimeOffset GeneratedAt);
