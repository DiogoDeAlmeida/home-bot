using System.Reflection;

namespace HomelabHub.Discord;

/// <summary>
/// Ancre typée vers cet assembly.
/// </summary>
/// <remarks>
/// <para>Sur Discord.Net 3.20.1 (ADR-0008). Ce qui existe :</para>
/// <list type="bullet">
///   <item><see cref="DiscordGatewayService"/> — passerelle, connexion sur jeton et serveur
///         configurés (<see cref="HomelabHub.Core.Configuration.HubSettings"/>), reste éteinte
///         sans eux plutôt que d'empêcher le hub de démarrer ;</item>
///   <item><see cref="Commands.DiscordCommandBuilder"/> — projection des capacités en slash
///         commands, une racine par module, y compris le pseudo-module <c>hub</c> ; un chemin
///         d'un segment devient une sous-commande directe, deux segments ouvrent un groupe ;</item>
///   <item><see cref="Commands.DiscordInteractionRoute"/> — le sens inverse, depuis une
///         interaction reçue jusqu'à la clé de capacité et ses arguments ;</item>
///   <item>autorisation tranchée ici (rôle configuré comparé aux rôles du membre) et transmise
///         au noyau, qui seul l'applique (ADR-0004) — jamais revérifiée dans une capacité.</item>
/// </list>
/// <para>Ce qui reste dû, dans cet ordre :</para>
/// <list type="bullet">
///   <item>boutons et flux de confirmation pour les <c>Mutation</c> à <c>RequireConfirmation</c> ;</item>
///   <item>message de tableau de bord persistant, édité en place, identifiant conservé via
///         <see cref="HomelabHub.Core.Configuration.HubSettings.DiscordDashboardMessageIdKey"/> ;</item>
///   <item>routage des transitions d'anomalie (ADR-0005) vers des notifications, aujourd'hui
///         seulement journalisées par <c>ModuleIngestionService</c> ;</item>
///   <item>migration de la validation de profondeur et de quotas depuis le noyau, dette assumée
///         par <c>CapabilityValidator</c> jusque-là (ADR-0016).</item>
/// </list>
/// </remarks>
public static class DiscordAdapterAssembly
{
    /// <summary>L'assembly de l'adaptateur Discord.</summary>
    public static Assembly Value => typeof(DiscordAdapterAssembly).Assembly;
}
