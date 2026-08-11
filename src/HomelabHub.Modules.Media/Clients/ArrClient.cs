using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using HomelabHub.Modules.Media.Contracts;

namespace HomelabHub.Modules.Media.Clients;

/// <summary>Radarr ou Sonarr — l'API v3 est la même, seuls les objets joints diffèrent.</summary>
public enum ArrFlavor
{
    Radarr = 0,
    Sonarr = 1,
}

/// <summary>Accès en lecture à une instance Radarr ou Sonarr.</summary>
public interface IArrClient
{
    ArrFlavor Flavor { get; }

    /// <summary>
    /// File d'attente complète.
    /// </summary>
    /// <remarks>
    /// <b>Un pack de saison produit un enregistrement par épisode</b>, tous porteurs du même
    /// <c>downloadId</c> (ADR-0015). Le regroupement est la première chose à faire du résultat.
    /// </remarks>
    Task<ServiceResult<IReadOnlyList<ArrQueueRecord>>> GetQueueAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Page d'historique récent, indexable par <c>downloadId</c>.
    /// </summary>
    /// <remarks>
    /// <b>Source de l'état terminal</b> : une entrée qui disparaît de la file peut avoir été
    /// importée, supprimée ou échouée, et seule l'historique le dit. Lue une fois par cycle,
    /// jamais une requête par parcours.
    /// </remarks>
    Task<ServiceResult<IReadOnlyList<ArrHistoryRecord>>> GetRecentHistoryAsync(
        int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// Historique d'un téléchargement précis. L'API filtre côté serveur.
    /// </summary>
    /// <remarks>
    /// <b>Repli, pas chemin nominal.</b> Une page d'historique a une taille finie : si plusieurs
    /// imports surviennent entre deux cycles, ou si un cycle échoue et que le suivant arrive
    /// tard, l'événement terminal d'un parcours peut être poussé hors de la page — et ce
    /// parcours resterait indéterminé pour toujours.
    /// <para>
    /// Cet appel ne se déclenche que sur ce cas précis : une entrée de file disparue sans
    /// qu'aucun événement terminal ait été vu. Il reste donc rare, et ne dégénère pas en une
    /// requête par parcours et par cycle.
    /// </para>
    /// </remarks>
    Task<ServiceResult<IReadOnlyList<ArrHistoryRecord>>> GetHistoryForDownloadAsync(
        string downloadId, CancellationToken cancellationToken);

    Task<ServiceResult<ArrSystemStatus>> GetSystemStatusAsync(CancellationToken cancellationToken);

    Task<ServiceResult<IReadOnlyList<ArrDiskSpace>>> GetDiskSpaceAsync(CancellationToken cancellationToken);

    Task<ServiceResult<IReadOnlyList<ArrHealthCheck>>> GetHealthAsync(CancellationToken cancellationToken);
}

/// <summary>Marqueur permettant au conteneur de distinguer les deux instances.</summary>
public interface IRadarrClient : IArrClient;

/// <summary>Marqueur permettant au conteneur de distinguer les deux instances.</summary>
public interface ISonarrClient : IArrClient;

/// <summary>
/// Implémentation partagée par Radarr et Sonarr.
/// </summary>
/// <remarks>
/// <para>
/// Deux clients distincts auraient dupliqué la pagination, la gestion d'erreur et la
/// désérialisation pour une différence qui se réduit à trois paramètres de requête. Les
/// captures le confirment : même enveloppe, mêmes noms de champs, seuls les objets joints
/// changent — <c>movie</c> d'un côté, <c>series</c> et <c>episode</c> de l'autre.
/// </para>
/// <para>
/// Aucun appel ne lève : une panne est rapportée par <see cref="ServiceResult{T}"/>
/// (convention §14).
/// </para>
/// </remarks>
internal abstract class ArrClient(HttpClient http, ArrFlavor flavor) : IArrClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ArrFlavor Flavor => flavor;

    /// <summary>
    /// <b>Toute la divergence entre Radarr et Sonarr tient ici, dans trois paramètres.</b>
    /// </summary>
    /// <remarks>
    /// C'est le premier endroit qui cassera à la sortie de Sonarr v5 ou d'un Radarr v7. Il est
    /// isolé et nommé pour cette raison, plutôt qu'enfoui dans une condition au milieu d'une
    /// méthode. Le reste de l'API v3 est réellement commun aux deux, captures à l'appui.
    /// </remarks>
    private static string QueueQuery(ArrFlavor flavor) => flavor switch
    {
        //                       ┌─ inclut les entrées sans média connu
        //                       │                            ┌─ joint le film / la série
        //                       │                            │       ┌─ joint l'épisode
        ArrFlavor.Radarr =>
            "includeUnknownMovieItems=true&includeMovie=true",
        ArrFlavor.Sonarr =>
            "includeUnknownSeriesItems=true&includeSeries=true&includeEpisode=true",
        _ => throw new ArgumentOutOfRangeException(nameof(flavor)),
    };

    public Task<ServiceResult<IReadOnlyList<ArrQueueRecord>>> GetQueueAsync(
        CancellationToken cancellationToken)
    {
        // Les objets joints coûtent en bande passante — une réponse Sonarr fait 236 Ko pour
        // 44 entrées — mais évitent un appel par média pour retrouver tmdbId et tvdbId.
        return GetPagedAsync<ArrQueueRecord>(
            $"api/v3/queue?pageSize=200&{QueueQuery(flavor)}", cancellationToken);
    }

    public Task<ServiceResult<IReadOnlyList<ArrHistoryRecord>>> GetHistoryForDownloadAsync(
        string downloadId, CancellationToken cancellationToken) =>
        GetPagedAsync<ArrHistoryRecord>(
            $"api/v3/history?downloadId={Uri.EscapeDataString(downloadId)}", cancellationToken);

    public Task<ServiceResult<IReadOnlyList<ArrHistoryRecord>>> GetRecentHistoryAsync(
        int pageSize, CancellationToken cancellationToken) =>
        GetPagedAsync<ArrHistoryRecord>(
            string.Create(CultureInfo.InvariantCulture,
                $"api/v3/history?pageSize={pageSize}&sortKey=date&sortDirection=descending"),
            cancellationToken);

    public Task<ServiceResult<ArrSystemStatus>> GetSystemStatusAsync(CancellationToken cancellationToken) =>
        GetAsync<ArrSystemStatus>("api/v3/system/status", cancellationToken);

    public Task<ServiceResult<IReadOnlyList<ArrDiskSpace>>> GetDiskSpaceAsync(
        CancellationToken cancellationToken) =>
        GetListAsync<ArrDiskSpace>("api/v3/diskspace", cancellationToken);

    public Task<ServiceResult<IReadOnlyList<ArrHealthCheck>>> GetHealthAsync(
        CancellationToken cancellationToken) =>
        GetListAsync<ArrHealthCheck>("api/v3/health", cancellationToken);

    private async Task<ServiceResult<T>> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            var value = await http.GetFromJsonAsync<T>(path, Json, cancellationToken).ConfigureAwait(false);
            return value is null
                ? ServiceResult.Fail<T>($"{flavor} a renvoyé une réponse vide pour {path}.")
                : ServiceResult.Ok(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return ServiceResult.Fail<T>(Describe(ex));
        }
    }

    private async Task<ServiceResult<IReadOnlyList<T>>> GetListAsync<T>(
        string path, CancellationToken cancellationToken)
    {
        var result = await GetAsync<List<T>>(path, cancellationToken).ConfigureAwait(false);

        return result.Success
            ? ServiceResult.Ok<IReadOnlyList<T>>(result.Value!)
            : ServiceResult.Fail<IReadOnlyList<T>>(result.Error!);
    }

    private async Task<ServiceResult<IReadOnlyList<T>>> GetPagedAsync<T>(
        string path, CancellationToken cancellationToken)
    {
        var result = await GetAsync<ArrPage<T>>(path, cancellationToken).ConfigureAwait(false);

        return result.Success
            ? ServiceResult.Ok<IReadOnlyList<T>>(result.Value!.Records)
            : ServiceResult.Fail<IReadOnlyList<T>>(result.Error!);
    }

    /// <summary>Message court et lisible : le journal porte déjà la pile d'appels.</summary>
    private string Describe(Exception ex) => ex switch
    {
        TaskCanceledException => $"{flavor} n'a pas répondu dans le délai imparti.",
        HttpRequestException http => $"{flavor} injoignable : {http.Message}",
        JsonException => $"{flavor} a renvoyé une réponse illisible — version d'API inattendue ?",
        _ => $"{flavor} : {ex.Message}",
    };
}

internal sealed class RadarrClient(HttpClient http) : ArrClient(http, ArrFlavor.Radarr), IRadarrClient;

internal sealed class SonarrClient(HttpClient http) : ArrClient(http, ArrFlavor.Sonarr), ISonarrClient;
