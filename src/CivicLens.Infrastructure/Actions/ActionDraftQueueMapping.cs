using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using CivicLens.Core.Relationships;
using CivicLens.Infrastructure.Relationships;
using CivicLens.Infrastructure.Actions.Persistence;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;

namespace CivicLens.Infrastructure.Actions;

internal sealed class ActionDraftQueueMapping(Func<CollectionAttemptDbContext, string, int, CancellationToken, Task> afterDecision)
    : IAnalysisQueueMapping<ActionAnalysisRow, ActionDraftRun>
{
    public string Table => PostgresActionDraftStore.Table;

    public string KeyColumn => "candidate_id";

    public string KeyProperty => nameof(ActionAnalysisRow.CandidateId);

    public string BudgetTable => "action_analysis_budget";

    public string? BudgetTask => ActionDraftTask.Task;

    public AnalysisTaskIdentity Identity => ActionDraftTask.Identity;

    public bool CanTransition(AnalysisRecord record, AnalysisCheckpoint checkpoint) => ActionDraftLifecycle.CanTransition(record, checkpoint);

    public AnalysisRun RunOf(ActionDraftRun run) => run.Run;

    public ActionDraftRun WrapInterrupted(AnalysisRun run) => new(run, null);

    public void AddRun(CollectionAttemptDbContext db, ActionAnalysisRow row, ActionDraftRun run)
    {
        var runRow = AnalysisRunRows.Create<ActionAnalysisRunRow>(run.Run);
        runRow.Reason = run.Reason;
        db.Add(runRow);
    }

    public async Task AfterTransitionAsync(CollectionAttemptDbContext db, ActionAnalysisRow row, AnalysisCheckpoint checkpoint, long now,
        CancellationToken cancellationToken)
    {
        if (checkpoint is not ActionDraftCheckpoint { Revision: { } revision }) return;
        row.Priority = await PairPriority.OfTargetAsync(db, RelationshipRecordKind.Action, revision.RecordId, !revision.TrackedIssueIds.IsEmpty,
            cancellationToken);
        await PostgresActionReviewStore.Records.AfterCodeApprovalAsync(db, revision.RecordId, revision.RevisionNumber, afterDecision, cancellationToken);
    }

    public async Task<AnalysisSettlement<ActionDraftRun>> PrepareSettlementAsync(CollectionAttemptDbContext db, ActionAnalysisRow row,
        AnalysisSettlement<ActionDraftRun> settlement, CancellationToken cancellationToken)
    {
        if (settlement.Run is null && settlement.Checkpoint.ErrorCode is ActionDraftErrorCodes.Unchanged or ActionDraftErrorCodes.NoTrackedOfficial)
            throw new ArgumentException("A bypassed settlement needs its run.", nameof(settlement));
        if (settlement.Checkpoint is not ActionDraftCheckpoint { Revision: { } revision } checkpoint || settlement.Run is not { } run)
            return settlement;
        if (run.Run.Outcome is not (AnalysisRunOutcome.Drafted or AnalysisRunOutcome.Bypassed))
            throw new ArgumentException("A revision needs its drafted or bypassed run.", nameof(settlement));
        var refusal = await ActionDraftRows.AppendAnalysisRevisionAsync(db, revision, run.Run.RunId, cancellationToken);
        if (refusal is null) return settlement;
        var discarded = run.Run.Outcome == AnalysisRunOutcome.Drafted
            ? run with { Run = run.Run with { Outcome = AnalysisRunOutcome.DraftDiscarded } } : run;
        if (refusal == ActionDraftErrorCodes.SupersededByNewerCandidate)
            return new(checkpoint with { ErrorCode = refusal, Revision = null }, discarded);
        row.RetryOfRunId = null;
        return new(checkpoint with
        {
            Status = AnalysisState.RetryWaiting,
            ErrorCode = refusal,
            RetryDelay = AnalysisPolicy.RetryDelay(row.Attempts),
            Revision = null
        }, discarded);
    }
}
