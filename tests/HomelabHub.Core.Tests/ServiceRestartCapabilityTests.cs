using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Core.Capabilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HomelabHub.Core.Tests;

/// <summary>
/// <c>hub.service.restart</c> : née d'un besoin concret trouvé en conditions réelles — la
/// configuration Discord n'est lue qu'au démarrage, jamais rechargée à chaud.
/// </summary>
/// <remarks>
/// Le délai réel avant l'arrêt (deux secondes, voir <see cref="ServiceRestartCapability"/>) n'est
/// délibérément pas attendu ici : le vérifier prouverait seulement qu'un <c>Task.Delay</c> attend
/// bien, pas que systemd relance effectivement le processus derrière — ça, seul le LXC jetable
/// peut le confirmer. Ce que ce test verrouille, c'est la propriété qui compte côté code :
/// l'arrêt n'a <b>pas</b> encore eu lieu au moment où l'appelant reçoit sa réponse.
/// </remarks>
public sealed class ServiceRestartCapabilityTests
{
    [Fact]
    public void La_capacite_exige_confirmation_et_sexpose_partout()
    {
        var descriptor = new ServiceRestartCapability(
            new RecordingLifetime(), NullLogger<ServiceRestartCapability>.Instance).Descriptor;

        Assert.Equal("hub.service.restart", descriptor.Key);
        Assert.Equal(CapabilityKind.Mutation, descriptor.Kind);
        Assert.Equal(CapabilityExposure.All, descriptor.Exposure);
        Assert.True(descriptor.RequireConfirmation);
        Assert.Equal(["service", "restart"], descriptor.Command!.Path);
    }

    [Fact]
    public async Task La_reponse_part_avant_que_larret_ne_soit_declenche()
    {
        var lifetime = new RecordingLifetime();
        var invocation = new CapabilityInvocation("hub.service.restart", new Dictionary<string, object?>(),
            InvocationSource.Api, "web:admin", IsAdministrator: true);

        var result = await new ServiceRestartCapability(lifetime, NullLogger<ServiceRestartCapability>.Instance)
            .ExecuteAsync(invocation, TestContext.Current.CancellationToken);

        Assert.Equal(CapabilityOutcome.Ok, result.Outcome);

        // Le point exact du bug qu'on évite : si StopApplication() était appelée avant de
        // renvoyer CapabilityResult, la réponse Discord ou REST courrait le risque de ne jamais
        // partir. Immédiatement après l'attente de ExecuteAsync, l'arrêt ne doit pas encore avoir
        // eu lieu — le vrai délai (deux secondes) reste en vol, hors de ce chemin.
        Assert.Equal(0, lifetime.StopCalls);
    }

    private sealed class RecordingLifetime : IHostApplicationLifetime
    {
        public int StopCalls { get; private set; }

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => StopCalls++;
    }
}
