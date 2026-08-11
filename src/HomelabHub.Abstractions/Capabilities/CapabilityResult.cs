namespace HomelabHub.Abstractions.Capabilities;

/// <summary>Issue d'un appel de capacité.</summary>
/// <param name="Outcome">Nature du résultat.</param>
/// <param name="Message">Message destiné à l'utilisateur, en français.</param>
/// <param name="Payload">
/// Données structurées, sans présentation. Chaque adaptateur les rend à sa façon : il n'y a
/// pas de modèle de rendu partagé entre Discord et le web (ADR-0006).
/// </param>
public sealed record CapabilityResult(
    CapabilityOutcome Outcome,
    string? Message = null,
    object? Payload = null)
{
    /// <summary>Exécuté, résultat connu.</summary>
    public static CapabilityResult Ok(object? payload = null) =>
        new(CapabilityOutcome.Ok, null, payload);

    /// <summary>Exécuté avec un message explicite et éventuellement des données.</summary>
    public static CapabilityResult Ok(string message, object? payload = null) =>
        new(CapabilityOutcome.Ok, message, payload);

    /// <summary>
    /// Ordre transmis, résultat inconnu.
    /// </summary>
    /// <remarks>
    /// Cas réel qui a imposé cette troisième valeur : un appel de service Home Assistant
    /// ne retourne rien d'exploitable. Prétendre au succès serait mentir, échouer aussi.
    /// </remarks>
    public static CapabilityResult Accepted(string message) =>
        new(CapabilityOutcome.Accepted, message);

    /// <summary>Échec attendu et explicable — service injoignable, argument invalide.</summary>
    public static CapabilityResult Fail(string message) =>
        new(CapabilityOutcome.Failed, message);
}

public enum CapabilityOutcome
{
    Ok = 0,
    Accepted = 1,
    Failed = 2,
}
