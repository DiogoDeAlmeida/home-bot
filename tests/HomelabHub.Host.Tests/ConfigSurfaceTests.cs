using HomelabHub.Abstractions.Configuration;
using HomelabHub.Core.Configuration;
using HomelabHub.Host.Api;
using Xunit;

namespace HomelabHub.Host.Tests;

/// <summary>
/// La projection et l'écriture partagées par la configuration d'un module et les réglages du
/// hub (ADR-0013).
/// </summary>
/// <remarks>
/// Déclenchés par un bug réel : après avoir touché un champ secret dans le formulaire — même
/// pour le laisser vide — l'enregistrement suivant, portant sur un tout autre champ, effaçait
/// le secret. Le formulaire ne propose pourtant aucun moyen de vider un secret délibérément :
/// un secret revenu vide ne doit donc jamais être traité comme « à effacer », seulement comme
/// « pas touché ».
/// </remarks>
public sealed class ConfigSurfaceTests
{
    private const string Prefix = "media";

    private static readonly IReadOnlyList<ConfigField> Fields = new ModuleConfigSchema()
        .AddSecret("radarr.apiKey", "Clé API Radarr", required: true)
        .AddInt("pollIntervalSeconds", "Intervalle d'interrogation", defaultValue: 60)
        .Fields;

    // ── Le cas rapporté ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Modifier_un_champ_non_secret_seul_laisse_les_secrets_intacts()
    {
        var store = new RecordingConfigStore();
        await store.SetAsync($"{Prefix}.radarr.apiKey", "03263cdb15ce44efac650e690ebad5c5", secret: true,
                             CancellationToken.None);

        // Le formulaire ne renvoie que ce qui a été modifié : le champ secret n'apparaît même
        // pas dans le payload, exactement comme quand seul l'intervalle a été retouché.
        var result = await ConfigSurface.WriteAsync(Prefix, Fields,
            new Dictionary<string, string?> { ["pollIntervalSeconds"] = "90" },
            store, CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NoContent>(result);
        Assert.Equal("03263cdb15ce44efac650e690ebad5c5", store.GetValue($"{Prefix}.radarr.apiKey"));
        Assert.Equal("90", store.GetValue($"{Prefix}.pollIntervalSeconds"));
    }

    // ── Défense en profondeur : même si le client envoie le champ vide quand même ────

    [Fact]
    public async Task Un_secret_envoye_vide_ne_supprime_pas_la_valeur_stockee()
    {
        // Le cas qui a produit le bug en pratique : le champ a été touché — une frappe suivie
        // d'un retour arrière suffit — et revient donc dans le payload, vide. Avant le correctif,
        // une valeur vide n'était pas null, elle passait le garde-fou du masque et écrasait le
        // secret par une chaîne vide chiffrée.
        var store = new RecordingConfigStore();
        await store.SetAsync($"{Prefix}.radarr.apiKey", "03263cdb15ce44efac650e690ebad5c5", secret: true,
                             CancellationToken.None);

        var result = await ConfigSurface.WriteAsync(Prefix, Fields,
            new Dictionary<string, string?> { ["radarr.apiKey"] = "" },
            store, CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NoContent>(result);
        Assert.Equal("03263cdb15ce44efac650e690ebad5c5", store.GetValue($"{Prefix}.radarr.apiKey"));
    }

    [Fact]
    public async Task Un_secret_envoye_a_null_ne_supprime_pas_la_valeur_stockee()
    {
        // Deuxième forme du même geste : le front convertit un champ vidé en `null` pour les
        // champs ordinaires (« effacer la clé »). Pour un secret, `null` ne doit pas non plus
        // être une suppression — il n'existe aucune façon, dans l'interface, de vider un secret
        // volontairement.
        var store = new RecordingConfigStore();
        await store.SetAsync($"{Prefix}.radarr.apiKey", "03263cdb15ce44efac650e690ebad5c5", secret: true,
                             CancellationToken.None);

        var result = await ConfigSurface.WriteAsync(Prefix, Fields,
            new Dictionary<string, string?> { ["radarr.apiKey"] = null },
            store, CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NoContent>(result);
        Assert.Equal("03263cdb15ce44efac650e690ebad5c5", store.GetValue($"{Prefix}.radarr.apiKey"));
    }

    [Fact]
    public async Task Un_secret_reaffiche_masque_ne_supprime_pas_la_valeur_stockee()
    {
        // Le piège d'origine, toujours couvert : un formulaire qui réémettrait le masque
        // affiché tel quel ne doit pas l'écrire en clair à la place du vrai secret.
        //
        // Le masque N'EST PAS entièrement composé du caractère de masque dès que le secret
        // dépasse quatre caractères — ReadForDisplay garde les quatre derniers en clair — donc
        // le masque réellement affiché est lu ici plutôt que supposé, pour que le test ne
        // dépende pas d'une forme qu'il devine.
        var store = new RecordingConfigStore();
        await store.SetAsync($"{Prefix}.radarr.apiKey", "03263cdb15ce44efac650e690ebad5c5", secret: true,
                             CancellationToken.None);

        var surface = ConfigSurface.Describe(Prefix, Fields, store);
        using var document = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(surface));
        var masked = document.RootElement.GetProperty("fields")[0].GetProperty("value").GetString()!;
        Assert.NotEqual("03263cdb15ce44efac650e690ebad5c5", masked);

        var result = await ConfigSurface.WriteAsync(Prefix, Fields,
            new Dictionary<string, string?> { ["radarr.apiKey"] = masked },
            store, CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NoContent>(result);
        Assert.Equal("03263cdb15ce44efac650e690ebad5c5", store.GetValue($"{Prefix}.radarr.apiKey"));
    }

    // ── Ce qui doit continuer à marcher ──────────────────────────────────────────────

    [Fact]
    public async Task Un_secret_envoye_avec_une_vraie_valeur_remplace_lancienne()
    {
        var store = new RecordingConfigStore();
        await store.SetAsync($"{Prefix}.radarr.apiKey", "ancienne-cle", secret: true, CancellationToken.None);

        await ConfigSurface.WriteAsync(Prefix, Fields,
            new Dictionary<string, string?> { ["radarr.apiKey"] = "nouvelle-cle-regeneree" },
            store, CancellationToken.None);

        Assert.Equal("nouvelle-cle-regeneree", store.GetValue($"{Prefix}.radarr.apiKey"));
    }

    [Fact]
    public async Task Un_champ_non_secret_envoye_a_null_supprime_la_cle()
    {
        // Comportement inchangé pour un champ ordinaire : c'est le seul cas où « null » doit
        // effectivement effacer quelque chose.
        var store = new RecordingConfigStore();
        await store.SetAsync($"{Prefix}.pollIntervalSeconds", "90", secret: false, CancellationToken.None);

        await ConfigSurface.WriteAsync(Prefix, Fields,
            new Dictionary<string, string?> { ["pollIntervalSeconds"] = null },
            store, CancellationToken.None);

        Assert.Null(store.GetValue($"{Prefix}.pollIntervalSeconds"));
    }

    [Fact]
    public async Task Un_champ_inconnu_est_rejete_sans_rien_ecrire()
    {
        var store = new RecordingConfigStore();

        var result = await ConfigSurface.WriteAsync(Prefix, Fields,
            new Dictionary<string, string?> { ["inexistant"] = "valeur" },
            store, CancellationToken.None);

        var statusResult = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>(result);
        Assert.Equal(400, statusResult.StatusCode);
        Assert.Null(store.GetValue($"{Prefix}.inexistant"));
    }

    /// <summary>Magasin en mémoire, assez fidèle pour éprouver la logique de fusion.</summary>
    private sealed class RecordingConfigStore : IHubConfigStore
    {
        private readonly Dictionary<string, ConfigValue> _entries = new(StringComparer.OrdinalIgnoreCase);

        public string? GetValue(string key) =>
            _entries.TryGetValue(key, out var entry) ? entry.Value : null;

        public Task SetAsync(string key, string? value, bool secret, CancellationToken cancellationToken) =>
            SetManyAsync(new Dictionary<string, ConfigValue> { [key] = new(value, secret) }, cancellationToken);

        public Task SetManyAsync(IReadOnlyDictionary<string, ConfigValue> values,
                                 CancellationToken cancellationToken)
        {
            foreach (var (key, entry) in values)
            {
                if (entry.Value is null)
                {
                    _entries.Remove(key);
                }
                else
                {
                    _entries[key] = entry;
                }
            }

            return Task.CompletedTask;
        }

        public IReadOnlyDictionary<string, string> GetByPrefix(string prefix) =>
            _entries.Where(e => e.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(e => e.Key, e => e.Value.Value ?? string.Empty);

        public bool IsSecret(string key) => _entries.TryGetValue(key, out var entry) && entry.Secret;
    }
}
