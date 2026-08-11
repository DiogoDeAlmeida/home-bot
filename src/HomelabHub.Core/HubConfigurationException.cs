namespace HomelabHub.Core;

/// <summary>
/// Une déclaration de module est invalide et le hub refuse de démarrer.
/// </summary>
/// <remarks>
/// Cette exception n'est levée qu'au démarrage, jamais en cours d'exécution. Une capacité mal
/// déclarée est une erreur de programmation : elle doit casser bruyamment au premier lancement,
/// pas produire une commande Discord silencieusement absente que l'on cherchera pendant une
/// heure.
/// </remarks>
public sealed class HubConfigurationException : Exception
{
    public HubConfigurationException(string message) : base(message)
    {
    }

    public HubConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public HubConfigurationException()
    {
    }

    /// <summary>Agrège plusieurs erreurs de déclaration en un seul message lisible.</summary>
    public static HubConfigurationException FromErrors(string context, IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var details = string.Join(Environment.NewLine, errors.Select(e => $"  · {e}"));
        return new HubConfigurationException(
            $"{context} — {errors.Count} problème(s) :{Environment.NewLine}{details}");
    }
}
