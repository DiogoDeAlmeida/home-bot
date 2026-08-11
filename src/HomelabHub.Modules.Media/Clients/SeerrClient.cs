using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using HomelabHub.Modules.Media.Contracts;

namespace HomelabHub.Modules.Media.Clients;

public interface ISeerrClient
{
    /// <summary>
    /// Requêtes les plus récemment modifiées.
    /// </summary>
    /// <remarks>
    /// Seules les clés de jointure amont sont exploitées — <c>externalServiceId</c>, <c>tmdbId</c>,
    /// <c>tvdbId</c>. Le <c>downloadStatus</c> que Seerr calcule de son côté est délibérément
    /// ignoré (ADR-0015) : il duplique par épisode et diverge de sa propre source.
    /// </remarks>
    Task<ServiceResult<IReadOnlyList<SeerrRequest>>> GetRecentRequestsAsync(
        int take, CancellationToken cancellationToken);

    Task<ServiceResult<SeerrStatus>> GetStatusAsync(CancellationToken cancellationToken);
}

internal sealed class SeerrClient(HttpClient http) : ISeerrClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ServiceResult<IReadOnlyList<SeerrRequest>>> GetRecentRequestsAsync(
        int take, CancellationToken cancellationToken)
    {
        var path = string.Create(CultureInfo.InvariantCulture,
            $"api/v1/request?take={take}&skip=0&sort=modified");

        var result = await GetAsync<SeerrPage<SeerrRequest>>(path, cancellationToken).ConfigureAwait(false);

        return result.Success
            ? ServiceResult.Ok<IReadOnlyList<SeerrRequest>>(result.Value!.Results)
            : ServiceResult.Fail<IReadOnlyList<SeerrRequest>>(result.Error!);
    }

    public Task<ServiceResult<SeerrStatus>> GetStatusAsync(CancellationToken cancellationToken) =>
        GetAsync<SeerrStatus>("api/v1/status", cancellationToken);

    private async Task<ServiceResult<T>> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            var value = await http.GetFromJsonAsync<T>(path, Json, cancellationToken).ConfigureAwait(false);
            return value is null
                ? ServiceResult.Fail<T>($"Seerr a renvoyé une réponse vide pour {path}.")
                : ServiceResult.Ok(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return ServiceResult.Fail<T>(ex switch
            {
                TaskCanceledException => "Seerr n'a pas répondu dans le délai imparti.",
                HttpRequestException http when http.StatusCode == System.Net.HttpStatusCode.Unauthorized =>
                    "Seerr a refusé la clé d'API.",
                HttpRequestException http => $"Seerr injoignable : {http.Message}",
                JsonException => "Seerr a renvoyé une réponse illisible — version d'API inattendue ?",
                _ => $"Seerr : {ex.Message}",
            });
        }
    }
}
