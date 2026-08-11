using System.Globalization;

namespace HomelabHub.Abstractions.Capabilities;

/// <summary>
/// Contexte d'un appel de capacité, construit par l'adaptateur et normalisé par le noyau.
/// </summary>
/// <param name="CapabilityKey">Capacité invoquée.</param>
/// <param name="Arguments">Arguments déjà validés contre <see cref="CapabilityDescriptor.Parameters"/>.</param>
/// <param name="Source">Adaptateur d'origine — utile pour adapter la verbosité, jamais pour décider d'un droit.</param>
/// <param name="ActorId">
/// Identité de l'appelant : identifiant Discord, ou <c>"web:admin"</c>. Sert au journal
/// d'audit des <see cref="CapabilityKind.Mutation"/>.
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

/// <summary>Adaptateur ayant émis l'appel.</summary>
public enum InvocationSource
{
    /// <summary>API REST de l'interface web.</summary>
    Rest = 0,

    /// <summary>Slash command Discord.</summary>
    DiscordCommand = 1,

    /// <summary>Bouton d'un message Discord. Aucune permission native côté Discord : le
    /// contrôle d'accès du noyau est ici la seule protection.</summary>
    DiscordComponent = 2,

    /// <summary>Déclenchement interne (planificateur, réaction à une anomalie).</summary>
    Internal = 3,
}
