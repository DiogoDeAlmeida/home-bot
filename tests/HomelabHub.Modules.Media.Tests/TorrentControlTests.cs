using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Modules.Media.Capabilities;
using Xunit;

namespace HomelabHub.Modules.Media.Tests;

/// <summary>
/// L'interruption et la reprise d'un torrent, câblées directement sur qBittorrent.
/// </summary>
/// <remarks>
/// Même gabarit que <c>ManualImportTests</c> : le descripteur est lu sur la vraie capacité, pas
/// sur une copie — c'est précisément ce dont le validateur de démarrage dépend, et ce qui
/// permet de le construire sans aucune dépendance résolue.
/// </remarks>
public sealed class TorrentControlTests
{
    [Fact]
    public void Linterruption_exige_une_confirmation_et_cible_qBittorrent()
    {
        var descriptor = new PauseDownloadCapability(null!, null!).Descriptor;

        Assert.Equal("media.download.pause", descriptor.Key);
        Assert.Equal(CapabilityKind.Mutation, descriptor.Kind);
        Assert.True(descriptor.RequireConfirmation);
        Assert.Equal(CapabilityExposure.All, descriptor.Exposure);
        Assert.Equal(["pause"], descriptor.Command!.Path);

        var parameter = Assert.Single(descriptor.Parameters);
        Assert.Equal("download", parameter.Name);
        Assert.True(parameter.Required);
    }

    [Fact]
    public void La_relance_exige_une_confirmation_et_cible_qBittorrent()
    {
        var descriptor = new ResumeDownloadCapability(null!, null!).Descriptor;

        Assert.Equal("media.download.resume", descriptor.Key);
        Assert.Equal(CapabilityKind.Mutation, descriptor.Kind);
        Assert.True(descriptor.RequireConfirmation);
        Assert.Equal(["resume"], descriptor.Command!.Path);
    }

    [Fact]
    public void Les_deux_cles_sont_distinctes()
    {
        // Détail qui semble trivial, sauf que les deux capacités partagent une base abstraite :
        // une erreur de copier-coller y laisserait la même clé sur les deux, et le registre de
        // capacités les rejetterait au démarrage pour doublon — mieux vaut le voir ici.
        var pause = new PauseDownloadCapability(null!, null!).Descriptor.Key;
        var resume = new ResumeDownloadCapability(null!, null!).Descriptor.Key;

        Assert.NotEqual(pause, resume);
    }
}
