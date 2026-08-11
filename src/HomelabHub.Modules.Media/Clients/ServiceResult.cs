namespace HomelabHub.Modules.Media.Clients;

/// <summary>
/// Résultat d'un appel à un service externe : une valeur, ou une raison d'échec.
/// </summary>
/// <remarks>
/// <para>
/// Convention §14 : un service injoignable dégrade l'affichage, il ne fait pas tomber le hub.
/// Les clients ne lèvent donc pas d'exception sur une panne réseau — ils la rapportent.
/// </para>
/// <para>
/// Une liste vide et un service injoignable ne sont pas la même chose. Confondre les deux
/// afficherait « aucun téléchargement » quand Radarr est éteint, ce qui est exactement le
/// mensonge qu'on veut éviter : la sonde de santé doit pouvoir distinguer les deux.
/// </para>
/// </remarks>
public sealed record ServiceResult<T>(T? Value, string? Error)
{
    public bool Success => Error is null;
}

/// <summary>
/// Fabriques et utilitaires. Séparés du type générique : CA1000 déconseille les membres
/// statiques sur un type générique, parce que l'appelant doit alors répéter le paramètre de
/// type sans y gagner.
/// </summary>
public static class ServiceResult
{
    public static ServiceResult<T> Ok<T>(T value) => new(value, null);

    public static ServiceResult<T> Fail<T>(string error) => new(default, error);

    /// <summary>Valeur, ou liste vide en cas d'échec — pour les usages qui tolèrent la dégradation.</summary>
    public static IReadOnlyList<T> OrEmpty<T>(this ServiceResult<IReadOnlyList<T>> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Value ?? [];
    }
}
