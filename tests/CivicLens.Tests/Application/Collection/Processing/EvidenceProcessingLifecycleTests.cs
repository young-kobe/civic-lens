using CivicLens.Application.Collection.Processing;

namespace CivicLens.Tests.Application.Collection.Processing;

public sealed class EvidenceProcessingLifecycleTests
{
    [Fact]
    public void ComparisonRequiresPersistedExtractionAndChangedOutcomeRequiresComparison()
    {
        var record = new EvidenceProcessingRecord("job", "attempt", "source", "https://example.test/",
            EvidenceProcessingStage.Comparison, EvidenceProcessingStatus.Running, "lease", 2, 1, null,
            new string('b', 64), null, null, null);
        var missingComparison = new EvidenceProcessingCheckpoint("job", "attempt", record.Stage, record.Status,
            "lease", 2, EvidenceProcessingStage.Complete, EvidenceProcessingStatus.Succeeded,
            Outcome: EvidenceProcessingOutcome.Changed);
        Assert.False(EvidenceProcessingLifecycle.CanTransition(record, missingComparison));

        var validChange = missingComparison with { ComparisonId = new string('d', 64) };
        Assert.True(EvidenceProcessingLifecycle.CanTransition(record, validChange));
        Assert.False(EvidenceProcessingLifecycle.CanTransition(record, validChange with { Fence = 1 }));
    }

    [Fact]
    public void ComparisonCannotReplaceItsEvidenceOrReportAnUnknownOutcome()
    {
        var record = new EvidenceProcessingRecord("job", "attempt", "source", "https://example.test/",
            EvidenceProcessingStage.Comparison, EvidenceProcessingStatus.Running, "lease", 2, 1, null,
            new string('b', 64), null, null, null);
        var change = new EvidenceProcessingCheckpoint("job", "attempt", record.Stage, record.Status,
            "lease", 2, EvidenceProcessingStage.Complete, EvidenceProcessingStatus.Succeeded,
            ComparisonId: new string('d', 64), Outcome: EvidenceProcessingOutcome.Changed);
        Assert.True(EvidenceProcessingLifecycle.CanTransition(record, change));
        Assert.False(EvidenceProcessingLifecycle.CanTransition(record, change with { ExtractionId = new string('c', 64) }));
        Assert.False(EvidenceProcessingLifecycle.CanTransition(record, change with { Outcome = (EvidenceProcessingOutcome)999 }));
        Assert.False(EvidenceProcessingLifecycle.CanTransition(record with { LeaseToken = "" }, change with { LeaseToken = "" }));
    }

    [Fact]
    public void FailureNeedsAnExplicitSafeCodeAndOutcome()
    {
        var record = new EvidenceProcessingRecord("job", "attempt", "source", "https://example.test/",
            EvidenceProcessingStage.Extraction, EvidenceProcessingStatus.Running, "lease", 1, 1, null,
            null, null, null, null);
        var malformed = new EvidenceProcessingCheckpoint("job", "attempt", record.Stage, record.Status,
            "lease", 1, EvidenceProcessingStage.Complete, EvidenceProcessingStatus.Blocked,
            Outcome: EvidenceProcessingOutcome.Blocked);
        Assert.False(EvidenceProcessingLifecycle.CanTransition(record, malformed));

        var valid = malformed with { ErrorCode = "configurationUnavailable" };
        Assert.True(EvidenceProcessingLifecycle.CanTransition(record, valid));
    }

    [Fact]
    public void RetryCheckpointRequiresBoundedNonnegativeDelayAndNoDelayForOtherStates()
    {
        var record = new EvidenceProcessingRecord("job", "attempt", "source", "https://example.test/",
            EvidenceProcessingStage.Extraction, EvidenceProcessingStatus.Running, "lease", 1, 1, null,
            null, null, null, null);
        var retry = new EvidenceProcessingCheckpoint("job", "attempt", record.Stage, record.Status,
            "lease", 1, record.Stage, EvidenceProcessingStatus.RetryWaiting, ErrorCode: "temporaryFailure",
            RetryDelay: TimeSpan.Zero);

        Assert.True(EvidenceProcessingLifecycle.CanTransition(record, retry));
        Assert.True(EvidenceProcessingLifecycle.CanTransition(record,
            retry with { RetryDelay = EvidenceProcessingPolicy.MaximumRetryDelay }));
        Assert.False(EvidenceProcessingLifecycle.CanTransition(record, retry with { RetryDelay = TimeSpan.FromTicks(-1) }));
        Assert.False(EvidenceProcessingLifecycle.CanTransition(record, retry with
        { RetryDelay = EvidenceProcessingPolicy.MaximumRetryDelay + TimeSpan.FromTicks(1) }));
        Assert.False(EvidenceProcessingLifecycle.CanTransition(record, retry with { RetryDelay = null }));
        Assert.False(EvidenceProcessingLifecycle.CanTransition(record, retry with
        { Status = EvidenceProcessingStatus.Failed, Outcome = EvidenceProcessingOutcome.Failed }));
    }
}
