using CivicLens.Application.Analysis;
using CivicLens.Application.Votes;
using CivicLens.Core.Analysis;
using CivicLens.Core.Relationships;
using CivicLens.Infrastructure.Relationships;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Votes.Persistence;

namespace CivicLens.Infrastructure.Votes;

internal sealed class VoteDraftQueueMapping(Func<CollectionAttemptDbContext, string, int, CancellationToken, Task> afterDecision)
    : IAnalysisQueueMapping<VoteAnalysisRow, VoteDraftRun>
{
    public string Table => PostgresVoteDraftStore.Table;

    public string KeyColumn => "candidate_id";

    public string KeyProperty => nameof(VoteAnalysisRow.CandidateId);

    public string BudgetTable => "vote_analysis_budget";

    public string? BudgetTask => VoteDraftTask.Task;

    public AnalysisTaskIdentity Identity => VoteDraftTask.Identity;

    public bool CanTransition(AnalysisRecord record, AnalysisCheckpoint checkpoint) => VoteDraftLifecycle.CanTransition(record, checkpoint);

    public AnalysisRun RunOf(VoteDraftRun run) => run.Run;

    public VoteDraftRun WrapInterrupted(AnalysisRun run) => new(run, null);

    public void AddRun(CollectionAttemptDbContext db, VoteAnalysisRow row, VoteDraftRun run)
    {
        var runRow = AnalysisRunRows.Create<VoteAnalysisRunRow>(run.Run);
        runRow.Reason = run.Reason;
        db.Add(runRow);
    }

    public async Task AfterTransitionAsync(CollectionAttemptDbContext db, VoteAnalysisRow row, AnalysisCheckpoint checkpoint, long now,
        CancellationToken cancellationToken)
    {
        if (checkpoint is not VoteDraftCheckpoint { Revision: { } revision }) return;
        row.Priority = await PairPriority.OfTargetAsync(db, RelationshipRecordKind.Vote, revision.RecordId, !revision.TrackedIssueIds.IsEmpty,
            cancellationToken);
        await VoteDraftRows.Records.AfterCodeApprovalAsync(db, revision.RecordId, revision.RevisionNumber, afterDecision, cancellationToken);
    }

    public async Task<AnalysisSettlement<VoteDraftRun>> PrepareSettlementAsync(CollectionAttemptDbContext db, VoteAnalysisRow row,
        AnalysisSettlement<VoteDraftRun> settlement, CancellationToken cancellationToken)
    {
        if (settlement.Run is null && settlement.Checkpoint.ErrorCode is VoteDraftErrorCodes.Unchanged or VoteDraftErrorCodes.NoTrackedMember)
            throw new ArgumentException("A bypassed settlement needs its run.", nameof(settlement));
        if (settlement.Run is { Reason: VoteDraftErrorCodes.RecordRejected } && settlement.Checkpoint is not VoteDraftCheckpoint { Revision: not null })
            return await ConfirmRefusalAsync(db, row, settlement, cancellationToken);
        if (settlement.Checkpoint is not VoteDraftCheckpoint { Revision: { } revision } checkpoint || settlement.Run is not { } run)
            return settlement;
        if (run.Run.Outcome is not (AnalysisRunOutcome.Drafted or AnalysisRunOutcome.Bypassed))
            throw new ArgumentException("A revision needs its drafted or bypassed run.", nameof(settlement));
        var refusal = await VoteDraftRows.AppendAnalysisRevisionAsync(db, revision, run.Run.RunId, cancellationToken);
        if (refusal is null) return settlement;
        var discarded = run.Run.Outcome == AnalysisRunOutcome.Drafted
            ? run with { Run = run.Run with { Outcome = AnalysisRunOutcome.DraftDiscarded } } : run;
        if (refusal == VoteDraftErrorCodes.RecordRejected)
            return new(checkpoint with { ErrorCode = VoteDraftErrorCodes.Unchanged, Revision = null }, discarded with { Reason = refusal });
        if (refusal == VoteDraftErrorCodes.SupersededByNewerCandidate)
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

    // The step read the record as rejected before it settled. A person may have changed it since, so the mark needs the review key.
    // The retry writes no run, because a run would hide an earlier mark.
    private static async Task<AnalysisSettlement<VoteDraftRun>> ConfirmRefusalAsync(CollectionAttemptDbContext db, VoteAnalysisRow row,
        AnalysisSettlement<VoteDraftRun> settlement, CancellationToken cancellationToken)
    {
        await PostgresAdvisoryLock.AcquireAsync(db, PostgresAdvisoryLock.VoteReview, cancellationToken);
        if (await VoteDraftRows.IsRejectedForCandidateAsync(db, row.CandidateId, cancellationToken)) return settlement;
        row.RetryOfRunId = null;
        return new(settlement.Checkpoint with
        {
            Status = AnalysisState.RetryWaiting,
            ErrorCode = VoteDraftErrorCodes.RecordRejected,
            RetryDelay = AnalysisPolicy.RetryDelay(row.Attempts)
        }, null);
    }
}
