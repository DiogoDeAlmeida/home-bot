using System.Diagnostics.CodeAnalysis;

namespace HomelabHub.Abstractions.Capabilities;

/// <summary>Description statique d'une capacité.</summary>
/// <param name="Key">
/// Identifiant unique, préfixé par la clé du module : <c>media.queue.pause</c>,
/// <c>system.backup.create</c>. Sert de route HTTP et de base aux identifiants de contrôles.
/// </param>
/// <param name="DisplayName">Libellé court, en français.</param>
/// <param name="Description">
/// Phrase descriptive, en français. Certains adaptateurs imposent une longueur maximale et
/// valident ce champ à leur démarrage.
/// </param>
/// <param name="Parameters">
/// Arguments attendus, volontairement limités aux types que les interfaces conversationnelles
/// savent exprimer.
/// </param>
/// <param name="Kind">
/// <see cref="CapabilityKind.Query"/> : lecture, ouverte à tous. <see cref="CapabilityKind.Mutation"/> :
/// modification, réservée aux administrateurs. C'est le noyau qui applique la règle, uniformément
/// à toutes les surfaces (ADR-0004).
/// </param>
/// <param name="Exposure">
/// Surfaces autorisées. Restreindre à <see cref="CapabilityExposure.Api"/> pour ce qui ne doit
/// jamais transiter par un canal conversationnel — <c>system.backup.create</c> produit une archive
/// contenant le keyring, donc toutes les clés d'API exploitables.
/// </param>
/// <param name="Command">
/// Nom de la commande, <b>indépendant de toute plateforme</b> (ADR-0016). <c>null</c> = pas de
/// commande. Doit être <c>null</c> si <paramref name="Exposure"/> exclut
/// <see cref="CapabilityExposure.Chat"/> : le validateur de démarrage échoue sur cette
/// contradiction plutôt que de choisir silencieusement.
/// </param>
/// <param name="RequireConfirmation">
/// Impose une confirmation avant exécution. <b>Propriété de l'opération, pas du canal</b> : une
/// suppression est destructrice qu'elle soit déclenchée depuis un bouton, une commande ou l'API.
/// Chaque adaptateur choisit comment demander cette confirmation.
/// </param>
public sealed record CapabilityDescriptor(
    string Key,
    string DisplayName,
    string Description,
    IReadOnlyList<CapabilityParameter> Parameters,
    CapabilityKind Kind,
    CapabilityExposure Exposure = CapabilityExposure.All,
    CommandBinding? Command = null,
    bool RequireConfirmation = false);

/// <summary>Nature de l'opération, dont le noyau dérive l'autorisation et la journalisation.</summary>
public enum CapabilityKind
{
    /// <summary>Lecture seule. Ouverte à tous les utilisateurs d'un canal conversationnel.</summary>
    Query = 0,

    /// <summary>Modifie un état externe. Réservée aux administrateurs, et journalisée.</summary>
    Mutation = 1,
}

/// <summary>Surfaces sur lesquelles une capacité a le droit d'apparaître.</summary>
/// <remarks>
/// Volontairement sans nom de plateforme (ADR-0016) : un drapeau par adaptateur aurait fait
/// grossir ce type d'une valeur à chaque canal ajouté, et aurait obligé chaque module existant
/// à se prononcer sur un canal qu'il ne connaît pas.
/// </remarks>
[Flags]
public enum CapabilityExposure
{
    None = 0,

    /// <summary>API HTTP, derrière l'authentification administrateur.</summary>
    Api = 1,

    /// <summary>Adaptateurs conversationnels : Discord aujourd'hui, un autre demain.</summary>
    Chat = 2,

    All = Api | Chat,
}

/// <summary>
/// Nom d'une capacité en tant que commande, indépendant de la plateforme.
/// </summary>
/// <remarks>
/// <para>
/// Le chemin est <b>relatif au module</b> : <c>["queue", "pause"]</c> dans le module
/// <c>media</c>. Chaque adaptateur le projette dans sa propre syntaxe — Discord en
/// <c>/media queue pause</c> (commande, groupe, sous-commande), un autre canal en
/// <c>/media_queue_pause</c>, une CLI en <c>media queue pause</c>.
/// </para>
/// <para>
/// <b>La capacité dit comment elle s'appelle, pas comment chaque plateforme l'épelle.</b>
/// C'est ce qui permet d'ajouter un adaptateur sans toucher ni au contrat, ni aux modules
/// (ADR-0016).
/// </para>
/// </remarks>
public sealed record CommandBinding
{
    /// <param name="path">Segments du chemin, relatifs au module. Au moins un.</param>
    public CommandBinding(params string[] path) => Path = path;

    /// <summary>Segments relatifs au module, par exemple <c>["queue", "pause"]</c>.</summary>
    public IReadOnlyList<string> Path { get; init; }

    /// <summary>
    /// La réponse n'est visible que de l'appelant, quand le canal sait le faire. À privilégier
    /// pour tout ce qui est verbeux ou personnel.
    /// </summary>
    public bool PrivateReply { get; init; }
}

/// <summary>
/// Argument d'une capacité. Les types sont délibérément restreints à ce que les interfaces
/// conversationnelles savent exprimer : une capacité qui aurait besoin d'un objet structuré ne
/// peut pas devenir une commande, et il vaut mieux s'en apercevoir à la conception.
/// </summary>
/// <param name="Name">Minuscules, <c>[a-z0-9_-]</c>, 32 caractères maximum.</param>
/// <param name="Description">En français, court.</param>
/// <param name="Type">Type de l'argument.</param>
/// <param name="Required">Les arguments obligatoires précèdent les optionnels.</param>
/// <param name="DefaultValue">Valeur retenue si l'argument est absent.</param>
/// <param name="Choices">Liste fermée de valeurs.</param>
public sealed record CapabilityParameter(
    string Name,
    string Description,
    CapabilityParameterType Type,
    bool Required = false,
    object? DefaultValue = null,
    IReadOnlyList<string>? Choices = null);

/// <remarks>
/// Les membres reprennent les types d'options des API conversationnelles courantes. Les
/// renommer pour satisfaire CA1720 masquerait la correspondance, qui est ce que ce type sert à
/// exprimer.
/// </remarks>
[SuppressMessage("Naming", "CA1720:Identifier contains type name",
    Justification = "Correspondance volontaire avec les types d'options des API conversationnelles.")]
public enum CapabilityParameterType
{
    String = 0,
    Integer = 1,
    Number = 2,
    Boolean = 3,
}
