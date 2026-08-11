using System.Reflection;

namespace HomelabHub.Discord;

/// <summary>
/// Ancre typée vers cet assembly.
/// </summary>
/// <remarks>
/// <para>Étape 0 : vide. Contenu prévu à l'étape 4, sur Discord.Net 3.20.1 (ADR-0008) :</para>
/// <list type="bullet">
///   <item>service d'arrière-plan tenant la passerelle, sans jamais laisser échapper d'exception ;</item>
///   <item>projection des capacités en slash commands, une racine par module
///         (<c>/media queue pause</c>), enregistrées en guild pour un effet immédiat ;</item>
///   <item>validateur de démarrage : profondeur, motif de nom, longueur de description,
///         quotas, et cohérence entre <c>Exposure</c> et <c>DiscordBinding</c> ;</item>
///   <item>message de dashboard persistant, édité en place, identifiant conservé en base ;</item>
///   <item><c>custom_id</c> stables, sans secret, survivant aux redémarrages.</item>
/// </list>
/// <para>
/// L'autorisation n'est <b>pas</b> implémentée ici : elle appartient au noyau (ADR-0004), qui
/// l'applique identiquement aux commandes, aux boutons et à l'API. Discord ne sait pas donner
/// de permission à une sous-commande, et n'en donne aucune aux composants de message.
/// </para>
/// </remarks>
public static class DiscordAdapterAssembly
{
    /// <summary>L'assembly de l'adaptateur Discord.</summary>
    public static Assembly Value => typeof(DiscordAdapterAssembly).Assembly;
}
