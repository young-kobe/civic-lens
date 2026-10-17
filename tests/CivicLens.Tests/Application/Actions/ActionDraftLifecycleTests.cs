using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Actions;

public sealed class ActionDraftLifecycleTests
{
    private const string CandidateId = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    [Fact]
    public void ABlockedActionDraftMayReturnToPendingSoTheOwnerCanRerunItAfterAFix()
    {
        var (record, requeue) = BlockedRequeue(ActionDraftTask.Task);

        Assert.True(ActionDraftLifecycle.CanTransition(record, requeue));
    }

    [Fact]
    public void ASucceededActionDraftStaysFinalBecauseNoCodeRequeuesIt()
    {
        var record = new AnalysisRecord(CandidateId, ActionDraftTask.Task, State.Succeeded, null, 1, 1, null, null);
        var requeue = new AnalysisCheckpoint(CandidateId, ActionDraftTask.Task, State.Succeeded, null, 1, State.Pending);

        Assert.False(ActionDraftLifecycle.CanTransition(record, requeue));
    }

    [Fact]
    public void GateStatementAndDocumentChangeTasksKeepBlockedFinalBecauseTheirLifecyclesDoNotOptIn()
    {
        var gate = BlockedRequeue(StatementGateTask.Task);
        var statementDraft = BlockedRequeue(StatementDraftTask.Task);
        var documentChange = BlockedRequeue(DocumentChangeDraftTask.Task);

        Assert.False(StatementGateLifecycle.CanTransition(gate.Record, gate.Requeue));
        Assert.False(StatementDraftLifecycle.CanTransition(statementDraft.Record, statementDraft.Requeue));
        Assert.False(DocumentChangeDraftLifecycle.CanTransition(documentChange.Record, documentChange.Requeue));
    }

    private static (AnalysisRecord Record, AnalysisCheckpoint Requeue) BlockedRequeue(string task) =>
        (new AnalysisRecord(CandidateId, task, State.Blocked, null, 1, 1, null, ActionDraftErrorCodes.TitleTooLong),
            new AnalysisCheckpoint(CandidateId, task, State.Blocked, null, 1, State.Pending));
}
