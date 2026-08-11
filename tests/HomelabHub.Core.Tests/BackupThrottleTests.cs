using HomelabHub.Core.Backup;
using Xunit;

namespace HomelabHub.Core.Tests;

/// <summary>
/// L'anti-rebond est la politique que le noyau applique aux demandes des modules (ADR-0014).
/// Sans lui, un détecteur nerveux produirait une archive à chaque cycle et saturerait le disque
/// qu'il surveille.
/// </summary>
public sealed class BackupThrottleTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    [Fact]
    public void La_premiere_demande_est_honoree()
    {
        var throttle = new BackupThrottle(new FixedTime(DateTimeOffset.UnixEpoch));

        Assert.True(throttle.TryAcquire(Interval, out var elapsed));
        Assert.Equal(TimeSpan.Zero, elapsed);
    }

    [Fact]
    public void Une_seconde_demande_trop_proche_est_refusee()
    {
        var time = new FixedTime(DateTimeOffset.UnixEpoch);
        var throttle = new BackupThrottle(time);

        throttle.TryAcquire(Interval, out _);
        time.Advance(TimeSpan.FromMinutes(2));

        Assert.False(throttle.TryAcquire(Interval, out var elapsed));
        Assert.Equal(TimeSpan.FromMinutes(2), elapsed);
    }

    [Fact]
    public void Une_demande_apres_lintervalle_est_honoree()
    {
        var time = new FixedTime(DateTimeOffset.UnixEpoch);
        var throttle = new BackupThrottle(time);

        throttle.TryAcquire(Interval, out _);
        time.Advance(Interval);

        Assert.True(throttle.TryAcquire(Interval, out _));
    }

    [Fact]
    public void Un_echec_ne_bloque_pas_la_demande_suivante()
    {
        // Sinon une sauvegarde ratée condamnerait le hub à rester sans sauvegarde pendant tout
        // l'intervalle, précisément quand quelque chose ne va pas.
        var time = new FixedTime(DateTimeOffset.UnixEpoch);
        var throttle = new BackupThrottle(time);

        throttle.TryAcquire(Interval, out _);
        throttle.Release();

        Assert.True(throttle.TryAcquire(Interval, out _));
    }

    [Fact]
    public void Lanti_rebond_est_global_et_non_par_module()
    {
        // Trois modules qui demandent chacun une sauvegarde dans la même minute n'en
        // justifient qu'une : ce qui compte est le disque, pas l'identité du demandeur.
        var throttle = new BackupThrottle(new FixedTime(DateTimeOffset.UnixEpoch));

        Assert.True(throttle.TryAcquire(Interval, out _));
        Assert.False(throttle.TryAcquire(Interval, out _));
        Assert.False(throttle.TryAcquire(Interval, out _));
    }

    private sealed class FixedTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
