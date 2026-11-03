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
        if (settlement.Run is { Reason: ActionDraftErrorCodes.RecordRejected } && settlement.Checkpoint is not ActionDraftCheckpoint { Revision: not null })
            return await ConfirmRefusalAsync(db, row, settlement, cancellationToken);
        if (settlement.Checkpoint is not ActionDraftCheckpoint { Revision: { } revision } checkpoint || settlement.Run is not { } run)
        {
            if (settlement.Checkpoint.ErrorCode is ActionDraftErrorCodes.Unchanged or ActionDraftErrorCodes.NoTrackedOfficial && settlement.Run is not null)
                await ClearRefusalAsync(db, row, cancellationToken);
            return settlement;
        }
        if (run.Run.Outcome is not (AnalysisRunOutcome.Drafted or AnalysisRunOutcome.Bypassed))
            throw new ArgumentException("A revision needs its drafted or bypassed run.", nameof(settlement));
        var refusal = await ActionDraftRows.AppendAnalysisRevisionAsync(db, revision, run.Run.RunId, cancellationToken);
        if (refusal is null) return settlement;
        var discarded = run.Run.Outcome == AnalysisRunOutcome.Drafted
            ? run with { Run = run.Run with { Outcome = AnalysisRunOutcome.DraftDiscarded } } : run;
        if (refusal == ActionDraftErrorCodes.RecordRejected)
            return new(checkpoint with { ErrorCode = ActionDraftErrorCodes.Unchanged, Revision = null }, discarded with { Reason = refusal });
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

    // The step read the record as rejected before it settled. A newer update or a person may have come since, so the block needs the review key.
    // A newer update supersedes this one with no run and no block. The retry writes no run, because the run would look like a settlement.
    private static async Task<AnalysisSettlement<ActionDraftRun>> ConfirmRefusalAsync(CollectionAttemptDbContext db, ActionAnalysisRow row,
        AnalysisSettlement<ActionDraftRun> settlement, CancellationToken cancellationToken)
    {
        await PostgresAdvisoryLock.AcquireAsync(db, PostgresAdvisoryLock.ActionReview, cancellationToken);
        if (await ActionDraftRows.IsSupersededAsync(db, row.CandidateId, cancellationToken))
            return new(settlement.Checkpoint with { ErrorCode = ActionDraftErrorCodes.SupersededByNewerCandidate }, null);
        if (await ActionDraftRows.ConfirmRefusalAsync(db, row.CandidateId, cancellationToken)) return settlement;
        row.RetryOfRunId = null;
        return new(settlement.Checkpoint with
        {
            Status = AnalysisState.RetryWaiting,
            ErrorCode = ActionDraftErrorCodes.RecordRejected,
            RetryDelay = AnalysisPolicy.RetryDelay(row.Attempts)
        }, null);
    }

    // An update with the same facts, or with no tracked official, ends the block. On a record that is rejected or holds a block, the review key comes first, so the settlement never reads "no block" while a refusal commits.
    private static async Task ClearRefusalAsync(CollectionAttemptDbContext db, ActionAnalysisRow row, CancellationToken cancellationToken)
    {
        if (!await ActionDraftRows.IsRejectedOrBlockedAsync(db, row.CandidateId, cancellationToken)) return;
        await PostgresAdvisoryLock.AcquireAsync(db, PostgresAdvisoryLock.ActionReview, cancellationToken);
        await ActionDraftRows.ClearRefusalAsync(db, row.CandidateId, cancellationToken);
    }
}
