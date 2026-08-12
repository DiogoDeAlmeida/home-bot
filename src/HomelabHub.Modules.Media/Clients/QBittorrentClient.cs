using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HomelabHub.Abstractions.Configuration;
using HomelabHub.Modules.Media.Contracts;

namespace HomelabHub.Modules.Media.Clients;

public interface IQBittorrentClient
{
    Task<ServiceResult<IReadOnlyList<QBittorrentTorrent>>> GetTorrentsAsync(CancellationToken cancellationToken);

    Task<ServiceResult<QBittorrentTransferInfo>> GetTransferInfoAsync(CancellationToken cancellationToken);

    Task<ServiceResult<string>> GetVersionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Interrompt un torrent.
    /// </summary>
    /// <remarks>
    /// <b>Seule écriture de ce client.</b> Sondé sans risque contre un hash inexistant sur
    /// l'instance réelle (5.1.0) : <c>stop</c>/<c>start</c> répondent 200, les anciens noms
    /// <c>pause</c>/<c>resume</c> répondent 404 — l'API a été renommée en 5.0. Un hash inconnu
    /// répond également 200 sans rien faire ; ce n'est donc pas ce statut qui dit si le torrent
    /// visé existait, seul l'état lu au cycle suivant le dira.
    /// </remarks>
    Task<ServiceResult<bool>> StopAsync(string hash, CancellationToken cancellationToken);

    /// <summary>Relance un torrent interrompu.</summary>
    Task<ServiceResult<bool>> StartAsync(string hash, CancellationToken cancellationToken);
}

/// <summary>
/// Client de la WebAPI v2 de qBittorrent 5.1.
/// </summary>
/// <remarks>
/// <para>
/// <b>Authentification par cookie, pas par clé d'API.</b> <c>/api/v2/auth/login</c> renvoie un
/// cookie <c>SID</c> qu'il faut ensuite présenter. Le client se reconnecte automatiquement sur
/// un 403, qui est la façon dont qBittorrent signale une session expirée.
/// </para>
/// <para>
/// L'installation cible contourne l'authentification pour son propre sous-réseau
/// (<c>AuthSubnetWhitelist</c>). Le flux complet est implémenté malgré tout : dépendre d'un
/// réglage réseau pour l'authentification, c'est se réveiller le jour où il change.
/// </para>
/// <para>
/// qBittorrent 4.2 et suivants rejettent les requêtes dont l'origine ne correspond pas. Un
/// en-tête <c>Referer</c> cohérent avec l'adresse de base est donc envoyé systématiquement.
/// </para>
/// </remarks>
internal sealed class QBittorrentClient(
    HttpClient http,
    IModuleConfiguration<MediaModule> config) : IQBittorrentClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<ServiceResult<IReadOnlyList<QBittorrentTorrent>>> GetTorrentsAsync(
        CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyList<QBittorrentTorrent>>("api/v2/torrents/info", cancellationToken);

    public Task<ServiceResult<QBittorrentTransferInfo>> GetTransferInfoAsync(
        CancellationToken cancellationToken) =>
        SendAsync<QBittorrentTransferInfo>("api/v2/transfer/info", cancellationToken);

    public async Task<ServiceResult<string>> GetVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Cette route répond en texte brut, pas en JSON.
            var version = await http.GetStringAsync("api/v2/app/version", cancellationToken)
                                    .ConfigureAwait(false);
            return ServiceResult.Ok(version.Trim());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ServiceResult.Fail<string>(Describe(ex));
        }
    }

    public Task<ServiceResult<bool>> StopAsync(string hash, CancellationToken cancellationToken) =>
        ControlAsync("api/v2/torrents/stop", hash, cancellationToken);

    public Task<ServiceResult<bool>> StartAsync(string hash, CancellationToken cancellationToken) =>
        ControlAsync("api/v2/torrents/start", hash, cancellationToken);

    /// <remarks>
    /// Même gabarit de réauthentification que <see cref="SendAsync{T}"/>, pour une réponse sans
    /// corps plutôt qu'un objet JSON : qBittorrent renvoie 200 avec un corps vide sur un succès.
    /// </remarks>
    private async Task<ServiceResult<bool>> ControlAsync(
        string path, string hash, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);

        try
        {
            var response = await PostHashAsync(path, hash, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                response.Dispose();

                if (!await AuthenticateAsync(cancellationToken).ConfigureAwait(false))
                {
                    return ServiceResult.Fail<bool>(
                        "qBittorrent a refusé l'authentification. Vérifier l'identifiant et le mot de passe.");
                }

                response = await PostHashAsync(path, hash, cancellationToken).ConfigureAwait(false);
            }

            using (response)
            {
                return response.IsSuccessStatusCode
                    ? ServiceResult.Ok(true)
                    : ServiceResult.Fail<bool>($"qBittorrent a répondu {(int)response.StatusCode} sur {path}.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ServiceResult.Fail<bool>(Describe(ex));
        }
    }

    /// <remarks>
    /// <c>using</c> à l'intérieur d'une méthode <c>async</c> qui attend l'envoi avant de
    /// retourner : le contenu du formulaire doit rester vivant jusqu'à ce que la requête soit
    /// effectivement écrite sur le fil, pas seulement jusqu'à ce que la tâche soit créée.
    /// </remarks>
    private async Task<HttpResponseMessage> PostHashAsync(
        string path, string hash, CancellationToken cancellationToken)
    {
        using var form = new FormUrlEncodedContent([new KeyValuePair<string, string>("hashes", hash)]);
        return await http.PostAsync(path, form, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ServiceResult<T>> SendAsync<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            var response = await http.GetAsync(path, cancellationToken).ConfigureAwait(false);

            // 403 = session absente ou expirée. On s'authentifie et on rejoue une seule fois :
            // boucler indéfiniment sur un mot de passe faux serait pire que d'échouer.
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                response.Dispose();

                if (!await AuthenticateAsync(cancellationToken).ConfigureAwait(false))
                {
                    return ServiceResult.Fail<T>(
                        "qBittorrent a refusé l'authentification. Vérifier l'identifiant et le mot de passe.");
                }

                response = await http.GetAsync(path, cancellationToken).ConfigureAwait(false);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    return ServiceResult.Fail<T>(
                        $"qBittorrent a répondu {(int)response.StatusCode} sur {path}.");
                }

                var value = await response.Content
                    .ReadFromJsonAsync<T>(Json, cancellationToken).ConfigureAwait(false);

                return value is null
                    ? ServiceResult.Fail<T>($"qBittorrent a renvoyé une réponse vide pour {path}.")
                    : ServiceResult.Ok(value);
            }
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

    /// <summary>
    /// Ouvre une session et laisse le cookie <c>SID</c> dans le <c>CookieContainer</c> partagé.
    /// </summary>
    /// <remarks>
    /// Sans verrou, délibérément : le client typé est <b>transitoire</b>, un sémaphore d'instance
    /// ne protégerait donc rien contre des appels concurrents. Deux connexions simultanées sont
    /// de toute façon inoffensives — qBittorrent délivre simplement deux cookies, et le dernier
    /// écrit gagne.
    /// </remarks>
    private async Task<bool> AuthenticateAsync(CancellationToken cancellationToken)
    {
        var username = config.GetString("qbittorrent.username");
        var password = config.GetString("qbittorrent.password");

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        try
        {
            using var form = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("username", username),
                new KeyValuePair<string, string>("password", password),
            ]);

            using var response = await http.PostAsync("api/v2/auth/login", form, cancellationToken)
                                           .ConfigureAwait(false);

            // qBittorrent répond 200 avec le corps « Fails. » quand les identifiants sont faux :
            // le code de statut seul ne suffit pas à conclure.
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                   && !body.Contains("Fails", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        TaskCanceledException => "qBittorrent n'a pas répondu dans le délai imparti.",
        HttpRequestException http =>
            $"qBittorrent injoignable : {http.Message} — si le tunnel WireGuard a redémarré, "
            + "vérifier la règle de routage qui exclut le LAN.",
        JsonException => "qBittorrent a renvoyé une réponse illisible — version d'API inattendue ?",
        _ => $"qBittorrent : {ex.Message}",
    };
}
