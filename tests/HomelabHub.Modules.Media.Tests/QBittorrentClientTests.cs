using HomelabHub.Abstractions.Configuration;
using HomelabHub.Modules.Media.Clients;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace HomelabHub.Modules.Media.Tests;

/// <summary>
/// Le comportement HTTP du client qBittorrent — réauthentification sur 403 — que les fixtures
/// ne peuvent pas couvrir puisqu'elles ne portent que la forme des réponses.
/// </summary>
/// <remarks>
/// <c>stop</c>/<c>start</c> sont la seule écriture du client (ADR sur l'import manuel, même
/// principe) : c'est le point où un cookie de session expiré doit se traduire par une
/// réauthentification silencieuse et un rejeu, pas par un échec visible de l'utilisateur.
/// </remarks>
public sealed class QBittorrentClientTests : IDisposable
{
    private const string Hash = "481b6e3617be4c88f96cb25e47c9d8272130071e";

    private readonly WireMockServer _server = WireMockServer.Start();

    public void Dispose() => _server.Stop();

    private QBittorrentClient NewClient(string? username = "admin", string? password = "secret")
    {
        var http = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        return new QBittorrentClient(http, new FakeConfiguration(username, password));
    }

    // ── Le chemin nominal ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Larret_reussi_envoie_le_hash_et_rapporte_un_succes()
    {
        _server
            .Given(Request.Create().WithPath("/api/v2/torrents/stop").UsingPost()
                          .WithBody(b => b?.Contains($"hashes={Hash}", StringComparison.Ordinal) == true))
            .RespondWith(Response.Create().WithStatusCode(200));

        var result = await NewClient().StopAsync(Hash, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task La_relance_reussie_rapporte_un_succes()
    {
        _server
            .Given(Request.Create().WithPath("/api/v2/torrents/start").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        var result = await NewClient().StartAsync(Hash, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
    }

    // ── Réauthentification ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Un_403_declenche_une_reauthentification_puis_un_rejeu_reussi()
    {
        // Le cas réel : la session a expiré entre deux cycles. Le premier appel échoue en 403,
        // le client se reconnecte, et le second essai — identique — doit réussir sans que
        // l'appelant n'ait rien à faire de plus.
        var attempt = 0;

        _server
            .Given(Request.Create().WithPath("/api/v2/torrents/stop").UsingPost())
            .RespondWith(Response.Create().WithCallback(_ =>
                new WireMock.ResponseMessage { StatusCode = ++attempt == 1 ? 403 : 200 }));

        _server
            .Given(Request.Create().WithPath("/api/v2/auth/login").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("Ok."));

        var result = await NewClient().StopAsync(Hash, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(2, attempt);
    }

    [Fact]
    public async Task Des_identifiants_refuses_echouent_proprement_sans_boucler()
    {
        _server
            .Given(Request.Create().WithPath("/api/v2/torrents/stop").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(403));

        _server
            .Given(Request.Create().WithPath("/api/v2/auth/login").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("Fails."));

        var result = await NewClient().StopAsync(Hash, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("identifiant", result.Error!, StringComparison.OrdinalIgnoreCase);

        // Une seule tentative de connexion : boucler sur un mot de passe faux serait pire que
        // d'échouer une fois.
        Assert.Single(_server.LogEntries, e => e.RequestMessage?.Path == "/api/v2/auth/login");
    }

    [Fact]
    public async Task Sans_identifiants_configures_lechec_est_immediat_sans_requete_de_connexion()
    {
        _server
            .Given(Request.Create().WithPath("/api/v2/torrents/stop").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(403));

        var result = await NewClient(username: null, password: null)
            .StopAsync(Hash, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.DoesNotContain(_server.LogEntries, e => e.RequestMessage?.Path == "/api/v2/auth/login");
    }

    // ── Échecs non authentification ─────────────────────────────────────────────────

    [Fact]
    public async Task Une_erreur_serveur_est_rapportee_sans_tentative_de_reauthentification()
    {
        _server
            .Given(Request.Create().WithPath("/api/v2/torrents/stop").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500));

        var result = await NewClient().StopAsync(Hash, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("500", result.Error!);
        Assert.DoesNotContain(_server.LogEntries, e => e.RequestMessage?.Path == "/api/v2/auth/login");
    }

    [Fact]
    public async Task Un_hash_vide_est_rejete_avant_tout_appel_reseau()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => NewClient().StopAsync(string.Empty, TestContext.Current.CancellationToken));
    }

    private sealed class FakeConfiguration(string? username, string? password)
        : IModuleConfiguration<MediaModule>
    {
        public bool IsComplete => true;

        public string? GetString(string key) => key switch
        {
            "qbittorrent.username" => username,
            "qbittorrent.password" => password,
            _ => null,
        };

        public bool GetBoolean(string key, bool fallback = false) => fallback;

        public int GetInt32(string key, int fallback = 0) => fallback;

        public TimeSpan GetDuration(string key, TimeSpan fallback) => fallback;
    }
}
