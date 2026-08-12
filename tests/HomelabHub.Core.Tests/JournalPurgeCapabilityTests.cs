using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Core.Anomalies;
using HomelabHub.Core.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HomelabHub.Core.Tests;

/// <summary>
/// <c>hub.journal.purge</c> : née d'un besoin de vérification concret — éprouver
/// <see cref="RetentionService"/> en conditions réelles sans attendre 24 heures — plutôt que
/// d'une anticipation.
/// </summary>
public sealed class JournalPurgeCapabilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void La_capacite_sinscrit_sous_le_prefixe_reserve_et_nexige_pas_de_confirmation()
    {
        var descriptor = new JournalPurgeCapability(NewRetentionService()).Descriptor;

        Assert.Equal("hub.journal.purge", descriptor.Key);
        Assert.Equal(CapabilityKind.Mutation, descriptor.Kind);
        Assert.False(descriptor.RequireConfirmation);
        Assert.Equal(["journal", "purge"], descriptor.Command!.Path);
    }

    [Fact]
    public async Task Executer_la_capacite_declenche_reellement_la_purge_et_rapporte_le_compte()
    {
        var journal = new RecordingJournalStore();
        journal.SetNextPurgeResult(3);
        var retention = NewRetentionService(journal);

        var invocation = new CapabilityInvocation("hub.journal.purge", new Dictionary<string, object?>(),
            InvocationSource.Api, "web:admin", IsAdministrator: true);

        var result = await new JournalPurgeCapability(retention)
            .ExecuteAsync(invocation, TestContext.Current.CancellationToken);

        Assert.Equal(CapabilityOutcome.Ok, result.Outcome);
        Assert.Contains("3", result.Message, StringComparison.Ordinal);
        Assert.Equal(1, journal.PurgeCalls);
    }

    private static RetentionService NewRetentionService(RecordingJournalStore? journal = null) => new(
        journal ?? new RecordingJournalStore(),
        new AnomalyEngine(new RecordingAnomalyStore(), NullLogger<AnomalyEngine>.Instance),
        new RecordingConfigStore(),
        new FixedTime(Now),
        NullLogger<RetentionService>.Instance);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
