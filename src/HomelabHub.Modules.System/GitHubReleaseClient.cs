using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace HomelabHub.Modules.SystemInfo;

/// <summary>Lit la dernière version publiée du dépôt, sans jamais rien déclencher elle-même.</summary>
/// <remarks>
/// Signalement seul (cadrage §7, étape 5) : le hub détient les clés de tout le homelab, une mise
/// à jour ne s'applique donc jamais d'elle-même. Cette classe ne fait que répondre à « existe-t-il
/// une version plus récente ? » — c'est <see cref="SystemPoller"/> qui décide quand demander, et
/// le journal d'anomalies qui décide comment le dire.
/// </remarks>
internal interface IGitHubReleaseClient
{
    /// <summary>
    /// Étiquette (<c>tag_name</c>) de la dernière publication, ou <c>null</c> si elle est
    /// indisponible — dépôt injoignable, aucune publication, ou réponse illisible. Ne lève jamais
    /// (convention §14).
    /// </summary>
    Task<string?> GetLatestReleaseTagAsync(CancellationToken cancellationToken);
}

internal sealed class GitHubReleaseClient(HttpClient http, ILogger<GitHubReleaseClient> logger)
    : IGitHubReleaseClient
{
    /// <summary>
    /// Fixe, pas configurable : c'est le dépôt du hub lui-même, pas un service externe que
    /// l'utilisateur pointerait ailleurs.
    /// </summary>
    private const string ReleasesPath = "repos/DiogoDeAlmeida/home-bot/releases/latest";

    public async Task<string?> GetLatestReleaseTagAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(ReleasesPath, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "Vérification de nouvelle version : GitHub a répondu {StatusCode}.",
                    (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return document.RootElement.TryGetProperty("tag_name", out var tag)
                ? tag.GetString()
                : null;
        }
        // Convention §14 : un dépôt injoignable ou une réponse mal formée dégrade la
        // vérification, elle ne fait jamais tomber le cycle qui l'a demandée.
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogInformation(ex, "Vérification de nouvelle version GitHub impossible.");
            return null;
        }
    }

    /// <summary>
    /// L'API GitHub exige un en-tête <c>User-Agent</c> identifiable, faute de quoi elle répond
    /// <c>403</c> sans distinction avec un dépassement de quota.
    /// </summary>
    internal static void Configure(HttpClient client)
    {
        client.BaseAddress = new Uri("https://api.github.com/");
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HomelabHub", "1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        // Convention §14 : jamais d'appel sortant sans délai d'attente explicite. Généreux parce
        // que peu fréquent (voir SystemModule.UpdateCheckIntervalHoursKey) — rien ne presse.
        client.Timeout = TimeSpan.FromSeconds(15);
    }
}
