using System.Diagnostics.CodeAnalysis;

namespace HomelabHub.Abstractions.Capabilities;

/// <summary>Description statique d'une capacité.</summary>
/// <param name="Key">
/// Identifiant unique, préfixé par la clé du module : <c>media.queue.pause</c>,
/// <c>system.backup.create</c>. Sert de route REST et de base au <c>custom_id</c> Discord.
/// </param>
/// <param name="DisplayName">Libellé court, en français.</param>
/// <param name="Description">
/// Phrase descriptive, en français. Limitée à 100 caractères si la capacité est exposée à
/// Discord — le validateur de démarrage le vérifie.
/// </param>
/// <param name="Parameters">Arguments attendus, volontairement limités aux types que Discord sait exprimer.</param>
/// <param name="Kind">
/// <see cref="CapabilityKind.Query"/> : lecture, ouverte à tous les membres.
/// <see cref="CapabilityKind.Mutation"/> : modification, réservée au rôle administrateur.
/// C'est le noyau qui applique cette règle, uniformément aux commandes, aux boutons et à
/// l'API — Discord ne sait pas donner de permission à une sous-commande, et n'en donne
/// aucune aux boutons (ADR-0004).
/// </param>
/// <param name="Exposure">
/// Surfaces autorisées. Défaut : REST et Discord. Restreindre à <see cref="CapabilityExposure.Rest"/>
/// pour ce qui ne doit jamais transiter par Discord — <c>system.backup.create</c> produit une
/// archive contenant le keyring, donc toutes les clés d'API exploitables : elle reste
/// derrière l'authentification admin de l'interface web.
/// </param>
/// <param name="Discord">
/// Projection en slash command. <c>null</c> = pas de commande. Doit être <c>null</c> si
/// <paramref name="Exposure"/> exclut Discord : le validateur de démarrage échoue sur cette
/// contradiction plutôt que de choisir silencieusement.
/// </param>
public sealed record CapabilityDescriptor(
    string Key,
    string DisplayName,
    string Description,
    IReadOnlyList<CapabilityParameter> Parameters,
    CapabilityKind Kind,
    CapabilityExposure Exposure = CapabilityExposure.All,
    DiscordBinding? Discord = null);

/// <summary>Nature de l'opération, dont le noyau dérive l'autorisation et la journalisation.</summary>
public enum CapabilityKind
{
    /// <summary>Lecture seule. Ouverte à tous les membres du serveur Discord.</summary>
    Query = 0,

    /// <summary>Modifie un état externe. Réservée au rôle administrateur, et journalisée.</summary>
    Mutation = 1,
}

/// <summary>Surfaces sur lesquelles une capacité a le droit d'apparaître.</summary>
[Flags]
public enum CapabilityExposure
{
    None = 0,

    /// <summary>API REST, derrière l'authentification admin.</summary>
    Rest = 1,

    /// <summary>Slash commands et boutons Discord.</summary>
    Discord = 2,

    All = Rest | Discord,
}

/// <summary>
/// Projection d'une capacité en slash command.
/// </summary>
/// <remarks>
/// Discord plafonne à trois niveaux : <c>commande → groupe → sous-commande</c>. La commande
/// racine est la clé du module, ce qui donne <c>/media queue pause</c> et laisse
/// exactement la place à <see cref="SubGroup"/> et <see cref="Name"/>. Une racine commune
/// <c>/hub</c> ferait un niveau de trop.
/// </remarks>
/// <param name="SubGroup">
/// Groupe de sous-commandes : <c>queue</c> dans <c>/media queue pause</c>. <c>null</c> pour
/// rattacher la sous-commande directement à la racine — <c>/system disk</c> n'a pas besoin
/// d'un groupe intermédiaire, et Discord accepte qu'une commande mélange sous-commandes et
/// groupes au même niveau.
/// </param>
/// <param name="Name">Sous-commande : <c>pause</c> dans <c>/media queue pause</c>.</param>
/// <param name="Ephemeral">Réponse visible du seul appelant. À privilégier pour tout ce qui est verbeux.</param>
/// <param name="RequireConfirmation">
/// Impose une confirmation par bouton avant exécution. Vient <i>en plus</i> du contrôle de
/// rôle induit par <see cref="CapabilityKind.Mutation"/>, pour les opérations destructrices.
/// </param>
public sealed record DiscordBinding(
    string? SubGroup,
    string Name,
    bool Ephemeral = false,
    bool RequireConfirmation = false);

/// <summary>
/// Argument d'une capacité. Les types sont délibérément restreints à ce que Discord sait
/// exprimer : une capacité qui aurait besoin d'un objet structuré ne peut pas devenir une
/// slash command, et il vaut mieux s'en apercevoir à la conception qu'au démarrage.
/// </summary>
/// <param name="Name">Minuscules, <c>[a-z0-9_-]</c>, 32 caractères maximum.</param>
/// <param name="Description">En français, 100 caractères maximum.</param>
/// <param name="Type">Type de l'argument.</param>
/// <param name="Required">Les arguments obligatoires précèdent les optionnels dans la commande générée.</param>
/// <param name="DefaultValue">Valeur retenue si l'argument est absent.</param>
/// <param name="Choices">Liste fermée de valeurs. 25 maximum, limite Discord.</param>
public sealed record CapabilityParameter(
    string Name,
    string Description,
    CapabilityParameterType Type,
    bool Required = false,
    object? DefaultValue = null,
    IReadOnlyList<string>? Choices = null);

/// <remarks>
/// Les membres portent délibérément les noms des types d'options Discord
/// (<c>STRING</c>, <c>INTEGER</c>, <c>NUMBER</c>, <c>BOOLEAN</c>). Les renommer pour
/// satisfaire CA1720 masquerait la correspondance, qui est précisément ce que ce type sert
/// à exprimer.
/// </remarks>
[SuppressMessage("Naming", "CA1720:Identifier contains type name",
    Justification = "Correspondance volontaire avec les types d'options de l'API Discord.")]
public enum CapabilityParameterType
{
    String = 0,
    Integer = 1,
    Number = 2,
    Boolean = 3,
}
