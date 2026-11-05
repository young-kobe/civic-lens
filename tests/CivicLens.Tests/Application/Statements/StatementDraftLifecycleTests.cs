using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Statements;

public sealed class StatementDraftLifecycleTests
{
    private const string CandidateId = "candidate";

    [Theory]
    [InlineData(null, true)]
    [InlineData(StatementDraftErrorCodes.HumanDraftExists, false)]
    [InlineData(StatementDraftErrorCodes.DraftDiscarded, false)]
    [InlineData(StatementDraftErrorCodes.OverByteCap, false)]
    public void OnlyASucceededStatementDraftThatHoldsAModelRevisionMayReturnToPendingForARedraft(string? errorCode, bool allowed)
    {
        var record = new AnalysisRecord(CandidateId, StatementDraftTask.Task, State.Succeeded, null, 1, 1, null, errorCode);
        var requeue = new AnalysisCheckpoint(CandidateId, StatementDraftTask.Task, State.Succeeded, null, 1, State.Pending);

        Assert.Equal(allowed, StatementDraftLifecycle.CanTransition(record, requeue));
    }
}
