using CivicLens.Application.Analysis;
using CivicLens.Application.Votes;
using CivicLens.Core.Analysis;
using CivicLens.Core.Relationships;
using CivicLens.Infrastructure.Relationships;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Votes.Persistence;

namespace CivicLens.Infrastructure.Votes;

internal sealed class VoteDraftQueueMapping : IAnalysisQueueMapping<VoteAnalysisRow, VoteDraftRun>
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
        if (checkpoint is VoteDraftCheckpoint { Revision: { } revision })
            row.Priority = await PairPriority.OfTargetAsync(db, RelationshipRecordKind.Vote, revision.RecordId, !revision.TrackedIssueIds.IsEmpty,
                cancellationToken);
    }

    public async Task<AnalysisSettlement<VoteDraftRun>> PrepareSettlementAsync(CollectionAttemptDbContext db, VoteAnalysisRow row,
        AnalysisSettlement<VoteDraftRun> settlement, CancellationToken cancellationToken)
    {
        if (settlement.Run is null && settlement.Checkpoint.ErrorCode is VoteDraftErrorCodes.Unchanged or VoteDraftErrorCodes.NoTrackedMember)
            throw new ArgumentException("A bypassed settlement needs its run.", nameof(settlement));
        if (settlement.Checkpoint is not VoteDraftCheckpoint { Revision: { } revision } checkpoint || settlement.Run is not { } run)
            return settlement;
        if (run.Run.Outcome is not (AnalysisRunOutcome.Drafted or AnalysisRunOutcome.Bypassed))
            throw new ArgumentException("A revision needs its drafted or bypassed run.", nameof(settlement));
        var refusal = await VoteDraftRows.AppendAnalysisRevisionAsync(db, revision, run.Run.RunId, cancellationToken);
        if (refusal is null) return settlement;
        var discarded = run.Run.Outcome == AnalysisRunOutcome.Drafted
            ? run with { Run = run.Run with { Outcome = AnalysisRunOutcome.DraftDiscarded } } : run;
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
}
