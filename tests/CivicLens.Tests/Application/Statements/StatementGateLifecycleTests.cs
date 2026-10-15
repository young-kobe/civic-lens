using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Statements;

public sealed class StatementGateLifecycleTests
{
    private const string CandidateId = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    [Theory]
    [InlineData(StatementGateReasons.NoTrackedIssue, true)]
    [InlineData(StatementGateReasons.Passed, false)]
    [InlineData(StatementGateReasons.OverByteCap, false)]
    [InlineData(StatementGateReasons.FailedOpen, false)]
    public void OnlyASkippedGateMayReturnToPendingForRegate(string reason, bool allowed)
    {
        var record = new AnalysisRecord(CandidateId, StatementGateTask.Task, State.Succeeded, null, 1, 0, null, reason);
        var requeue = new AnalysisCheckpoint(CandidateId, StatementGateTask.Task, State.Succeeded, null, 1, State.Pending);

        Assert.Equal(allowed, StatementGateLifecycle.CanTransition(record, requeue));
    }

    [Theory]
    [InlineData(StatementGateReasons.Passed, true)]
    [InlineData(StatementGateReasons.NoTrackedIssue, true)]
    [InlineData("draftDiscarded", false)]
    [InlineData(null, false)]
    public void ASucceededGateNeedsAKnownReason(string? reason, bool allowed)
    {
        var record = new AnalysisRecord(CandidateId, StatementGateTask.Task, State.Running, "lease", 1, 0, null, null);
        var settle = new AnalysisCheckpoint(CandidateId, StatementGateTask.Task, State.Running, "lease", 1, State.Succeeded, reason);

        Assert.Equal(allowed, StatementGateLifecycle.CanTransition(record, settle));
    }

    [Fact]
    public void ADocumentChangeEntryNeverReturnsFromSucceededToPending()
    {
        var record = new AnalysisRecord(CandidateId, DocumentChangeDraftTask.Task, State.Succeeded, null, 1, 0, null, StatementGateReasons.NoTrackedIssue);
        var requeue = new AnalysisCheckpoint(CandidateId, DocumentChangeDraftTask.Task, State.Succeeded, null, 1, State.Pending);

        Assert.False(DocumentChangeDraftLifecycle.CanTransition(record, requeue));
    }
}
