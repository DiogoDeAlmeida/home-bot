using Xunit;

namespace HomelabHub.Modules.Media.Tests;

/// <summary>
/// La clé de corrélation et le piège d'agrégation, vérifiés sur les données réelles.
/// </summary>
/// <remarks>
/// Ces tests figent ce que la capture a établi (ADR-0015). Ils ne testent pas encore la
/// corrélation elle-même — elle n'est pas écrite — mais ils garantissent que les prémisses sur
/// lesquelles elle sera construite restent vraies.
/// </remarks>
public sealed class CorrelationKeyTests
{
    private const string SonarrMixed = "Lifecycle/13-sonarr-queue-downloading-et-importpending.json";
    private const string RadarrDownloading = "Lifecycle/02-radarr-queue-downloading.json";
    private const string Torrents = "Lifecycle/21-qbittorrent-downloading.json";

    [Fact]
    public void Le_downloadId_est_le_hash_du_torrent_en_majuscules()
    {
        foreach (var record in Fixture.Queue(SonarrMixed).Concat(Fixture.Queue(RadarrDownloading)))
        {
            Assert.NotNull(record.DownloadId);
            Assert.Equal(40, record.DownloadId!.Length);
            Assert.Equal(record.DownloadId.ToUpperInvariant(), record.DownloadId);
        }
    }

    [Fact]
    public void Le_hash_qBittorrent_est_le_meme_en_minuscules()
    {
        foreach (var torrent in Fixture.Torrents(Torrents))
        {
            Assert.Equal(torrent.Hash.ToLowerInvariant(), torrent.Hash);
            Assert.Equal(torrent.Hash, torrent.JoinKey);
        }
    }

    [Fact]
    public void La_jointure_file_vers_torrent_aboutit_apres_normalisation_de_casse()
    {
        var torrents = Fixture.Torrents(Torrents).ToDictionary(t => t.JoinKey);
        var records = Fixture.Queue(SonarrMixed).Concat(Fixture.Queue(RadarrDownloading)).ToList();

        Assert.All(records, record =>
            Assert.True(torrents.ContainsKey(record.DownloadId!.ToLowerInvariant()),
                $"Aucun torrent pour {record.DownloadId}."));
    }

    [Fact]
    public void Un_pack_de_saison_produit_un_enregistrement_par_episode()
    {
        // Le fait constaté : 44 enregistrements pour 2 torrents, 22 chacun.
        var records = Fixture.Queue(SonarrMixed);
        var groups = records.GroupBy(r => r.DownloadId).ToList();

        Assert.Equal(44, records.Count);
        Assert.Equal(2, groups.Count);
        Assert.All(groups, group => Assert.Equal(22, group.Count()));

        // Chaque enregistrement du groupe vise un épisode distinct, avec la même taille répétée.
        foreach (var group in groups)
        {
            Assert.Equal(22, group.Select(r => r.EpisodeId).Distinct().Count());
            Assert.Single(group.Select(r => r.Size).Distinct());
        }
    }

    [Fact]
    public void Agreger_sans_regrouper_par_downloadId_donne_un_resultat_absurde()
    {
        // C'est le test qui documente le piège. 451 Go affichés là où il y en a 20 : absurde à
        // l'écran, indiagnosticable sans comprendre la duplication (ADR-0015).
        var records = Fixture.Queue(SonarrMixed);

        var naive = records.Sum(r => r.Size);
        var correct = records.GroupBy(r => r.DownloadId).Sum(g => g.First().Size);

        Assert.Equal(451_022_706_508, naive);
        Assert.Equal(20_501_032_114, correct);
        Assert.Equal(22, naive / correct);
    }

    [Fact]
    public void La_progression_agregee_se_calcule_sur_les_octets_pas_sur_les_pourcentages()
    {
        // Moyenner les pourcentages de deux torrents de tailles très différentes donnerait une
        // progression qui ne correspond à rien. La somme des octets restants, elle, a un sens.
        var groups = Fixture.Queue(SonarrMixed)
            .GroupBy(r => r.DownloadId)
            .Select(g => g.First())
            .ToList();

        var total = groups.Sum(r => r.Size);
        var left = groups.Sum(r => r.SizeLeft);

        Assert.True(total > 0);
        Assert.InRange((double)(total - left) / total, 0d, 1d);
    }
}
