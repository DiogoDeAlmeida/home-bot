using System.Globalization;

namespace HomelabHub.Abstractions.Capabilities;

/// <summary>
/// Contexte d'un appel de capacité, construit par l'adaptateur et normalisé par le noyau.
/// </summary>
/// <param name="CapabilityKey">Capacité invoquée.</param>
/// <param name="Arguments">Arguments déjà validés contre <see cref="CapabilityDescriptor.Parameters"/>.</param>
/// <param name="Source">Genre de surface — utile pour adapter la verbosité, jamais pour décider d'un droit.</param>
/// <param name="ActorId">
/// Identité opaque de l'appelant, préfixée par l'adaptateur : <c>"discord:123456"</c>,
/// <c>"web:admin"</c>. Le noyau ne l'interprète jamais ; il la journalise pour l'audit des
/// <see cref="CapabilityKind.Mutation"/>. C'est ce qui permet d'ajouter un adaptateur sans
/// toucher au modèle d'identité (ADR-0016).
/// </param>
/// <param name="IsAdministrator">
/// Autorisation déjà tranchée par le noyau. Une capacité n'a pas à la revérifier ; le champ
/// est présent pour adapter un affichage, pas pour protéger une opération.
/// </param>
public sealed record CapabilityInvocation(
    string CapabilityKey,
    IReadOnlyDictionary<string, object?> Arguments,
    InvocationSource Source,
    string ActorId,
    bool IsAdministrator)
{
    /// <summary>Lit un argument, ou renvoie <paramref name="fallback"/> s'il est absent ou vide.</summary>
    public string GetString(string name, string fallback = "") =>
        Arguments.TryGetValue(name, out var value) && value is not null
            ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? fallback
            : fallback;

    /// <summary>Lit un argument entier, ou renvoie <paramref name="fallback"/>.</summary>
    public long GetInteger(string name, long fallback = 0) =>
        Arguments.TryGetValue(name, out var value) && value is not null
            ? Convert.ToInt64(value, CultureInfo.InvariantCulture)
            : fallback;

    /// <summary>Lit un argument booléen, ou renvoie <paramref name="fallback"/>.</summary>
    public bool GetBoolean(string name, bool fallback = false) =>
        Arguments.TryGetValue(name, out var value) && value is not null
            ? Convert.ToBoolean(value, CultureInfo.InvariantCulture)
            : fallback;
}

/// <summary>Nature de la surface ayant émis l'appel.</summary>
/// <remarks>
/// Sans nom de plateforme (ADR-0016) : ce qui compte pour le noyau est le <i>genre</i> de
/// surface, pas son nom commercial. Le nom concret de l'adaptateur, lui, voyage dans
/// <see cref="CapabilityInvocation.ActorId"/> et sert au journal d'audit.
/// </remarks>
public enum InvocationSource
{
    /// <summary>API HTTP de l'interface web.</summary>
    Api = 0,

    /// <summary>Commande tapée dans un canal conversationnel.</summary>
    ChatCommand = 1,

    /// <summary>
    /// Bouton d'un message. Les plateformes conversationnelles n'offrent en général aucun
    /// contrôle d'accès sur ces contrôles : la vérification du noyau est ici la seule protection.
    /// </summary>
    ChatButton = 2,

    /// <summary>Déclenchement interne (planificateur, réaction à une anomalie).</summary>
    Internal = 3,
}
