using HomelabHub.Abstractions.Events;
using HomelabHub.Modules.Media.Contracts;
using HomelabHub.Modules.Media.Correlation;
using HomelabHub.Modules.Media.Detection;
using Xunit;

namespace HomelabHub.Modules.Media.Tests;

/// <summary>
/// Les seuils de détection, chacun vérifié des deux côtés de sa frontière.
/// </summary>
/// <remarks>
/// Les détecteurs sont une fonction pure du snapshot : ni horloge, ni réseau, ni état. C'est ce
/// qui permet de placer une observation à la minute près sans attendre.
/// </remarks>
public sealed class DetectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    private static readonly DetectionThresholds Thresholds =
        new(StalledAfter: TimeSpan.FromMinutes(30), GraceAfterAdded: TimeSpan.FromMinutes(10));

    private static MediaSnapshot Snapshot(DownloadItem download) =>
        new([new MediaJourney("movie:1", MediaKind.Movie, "Titre", null, null, null,
                              [download], JourneyState.Downloading)], [], Now);

    private static DownloadItem Download(
        TimeSpan idleFor,
        TimeSpan addedAgo,
        bool progressed,
        DownloadState state = DownloadState.Downloading,
        DownloadHealth health = DownloadHealth.Ok) =>
        new(
            DownloadId: "aa",
            Title: "release",
            Size: 1000,
            SizeLeft: progressed ? 400 : 1000,
            State: state,
            Health: health,
            Torrent: new QBittorrentTorrent
            {
                Hash = "aa",
                LastActivity = Now.Subtract(idleFor).ToUnixTimeSeconds(),
                NumSeeds = 0,
            },
            Episodes: [],
            AddedAt: Now.Subtract(addedAgo),
            Terminal: null);

    private static IReadOnlyList<HubEvent> Detect(DownloadItem download) =>
        MediaDetectors.Detect(Snapshot(download), Thresholds, Now);

    // ── Blocage ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Un_telechargement_actif_recemment_nest_pas_signale()
    {
        var events = Detect(Download(idleFor: TimeSpan.FromMinutes(29),
                                     addedAgo: TimeSpan.FromHours(2), progressed: true));

        Assert.DoesNotContain(events, e => e.Type == "media.download.stalled");
    }

    [Fact]
    public void Un_telechargement_inactif_depuis_le_seuil_est_signale()
    {
        var events = Detect(Download(idleFor: TimeSpan.FromMinutes(31),
                                     addedAgo: TimeSpan.FromHours(2), progressed: true));

        var anomaly = Assert.Single(events, e => e.Type == "media.download.stalled");
        Assert.Equal(HubEventSeverity.Warning, anomaly.Severity);
        Assert.Equal("media.download.stalled:aa", anomaly.DedupeKey);
    }

    [Fact]
    public void Un_torrent_tout_juste_ajoute_et_jamais_actif_beneficie_du_delai_de_grace()
    {
        // Le faux positif de la première seconde : « stalled with no connections » avant même
        // d'avoir trouvé un pair. Sans ce délai, chaque grab lèverait une alerte.
        var events = Detect(Download(idleFor: TimeSpan.FromHours(1),
                                     addedAgo: TimeSpan.FromMinutes(9), progressed: false));

        Assert.DoesNotContain(events, e => e.Type == "media.download.stalled");
    }

    [Fact]
    public void Passe_le_delai_de_grace_un_torrent_jamais_actif_est_signale()
    {
        var events = Detect(Download(idleFor: TimeSpan.FromHours(1),
                                     addedAgo: TimeSpan.FromMinutes(11), progressed: false));

        Assert.Contains(events, e => e.Type == "media.download.stalled");
    }

    [Fact]
    public void Un_torrent_ayant_deja_progresse_ne_beneficie_pas_du_delai_de_grace()
    {
        // Il a progressé, donc il a trouvé des pairs : son silence n'est plus un démarrage lent.
        var events = Detect(Download(idleFor: TimeSpan.FromMinutes(31),
                                     addedAgo: TimeSpan.FromMinutes(2), progressed: true));

        Assert.Contains(events, e => e.Type == "media.download.stalled");
    }

    [Fact]
    public void Sans_torrent_connu_linactivite_nest_pas_devinee()
    {
        var orphan = Download(TimeSpan.FromHours(5), TimeSpan.FromHours(5), progressed: true)
            with { Torrent = null };

        Assert.DoesNotContain(Detect(orphan), e => e.Type == "media.download.stalled");
    }

    [Fact]
    public void Une_horloge_decalee_ne_declenche_pas_dalerte()
    {
        // last_activity dans le futur : la soustraction est absurde, on ne conclut rien.
        var skewed = Download(idleFor: TimeSpan.FromMinutes(-45),
                              addedAgo: TimeSpan.FromHours(2), progressed: true);

        Assert.DoesNotContain(Detect(skewed), e => e.Type == "media.download.stalled");
    }

    // ── Import en attente ────────────────────────────────────────────────────────────

    [Fact]
    public void Un_import_en_attente_est_signale_des_le_premier_cycle_sans_seuil()
    {
        // L'inversion : dans le cas nominal cette fenêtre dure moins de cinq secondes, donc
        // moins que l'intervalle de polling. La voir signifie qu'elle persiste.
        var events = Detect(Download(idleFor: TimeSpan.Zero, addedAgo: TimeSpan.FromSeconds(30),
                                     progressed: true, state: DownloadState.Importing));

        var anomaly = Assert.Single(events, e => e.Type == "media.import.pending");
        Assert.Equal("media.import.pending:aa", anomaly.DedupeKey);
    }

    // ── Santé rapportée ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(DownloadHealth.Warning)]
    [InlineData(DownloadHealth.Error)]
    public void Une_sante_degradee_est_signalee_immediatement_mais_en_avertissement(DownloadHealth health)
    {
        // Toujours en avertissement, jamais en erreur : tant que statusMessages n'a pas été
        // observé sur un cas bloqué, on ne sait pas interpréter la gravité.
        var events = Detect(Download(TimeSpan.Zero, TimeSpan.FromHours(1), true, health: health));

        var anomaly = Assert.Single(events, e => e.Type == "media.download.unhealthy");
        Assert.Equal(HubEventSeverity.Warning, anomaly.Severity);
    }

    [Fact]
    public void Une_sante_ok_ne_produit_rien()
    {
        var events = Detect(Download(TimeSpan.FromMinutes(1), TimeSpan.FromHours(1), true));

        Assert.Empty(events);
    }

    // ── Comportement d'ensemble ──────────────────────────────────────────────────────

    [Fact]
    public void Les_detecteurs_republient_tout_a_chaque_appel_sans_memoire()
    {
        // Projection sans état : deux appels identiques produisent exactement la même chose.
        // C'est ce qui permet au noyau de résoudre par l'absence (ADR-0005).
        var download = Download(TimeSpan.FromMinutes(45), TimeSpan.FromHours(2), true);

        var first = MediaDetectors.Detect(Snapshot(download), Thresholds, Now);
        var second = MediaDetectors.Detect(Snapshot(download), Thresholds, Now);

        Assert.Equal(first.Select(e => e.DedupeKey), second.Select(e => e.DedupeKey));
    }

    [Fact]
    public void Chaque_anomalie_porte_une_cle_de_deduplication_stable()
    {
        var events = Detect(Download(TimeSpan.FromMinutes(45), TimeSpan.FromHours(2), true,
                                     health: DownloadHealth.Warning));

        Assert.All(events, e => Assert.False(string.IsNullOrWhiteSpace(e.DedupeKey)));
        Assert.Equal(events.Select(e => e.DedupeKey).Distinct().Count(), events.Count);
    }
}
