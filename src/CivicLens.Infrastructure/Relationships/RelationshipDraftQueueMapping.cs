using CivicLens.Application.Analysis;
using CivicLens.Application.Relationships;
using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Relationships.Persistence;

namespace CivicLens.Infrastructure.Relationships;

internal sealed class RelationshipDraftQueueMapping : IAnalysisQueueMapping<RelationshipAnalysisRow, RelationshipDraftRun>
{
    public string Table => PostgresRelationshipDraftStore.Table;

    public string KeyColumn => "pair_id";

    public string KeyProperty => nameof(RelationshipAnalysisRow.PairId);

    public string BudgetTable => "relationship_analysis_budget";

    public string? BudgetTask => RelationshipDraftTask.Task;

    public AnalysisTaskIdentity Identity => RelationshipDraftTask.Identity;

    public bool CanTransition(AnalysisRecord record, AnalysisCheckpoint checkpoint) =>
        RelationshipDraftLifecycle.CanTransition(record, checkpoint);

    public AnalysisRun RunOf(RelationshipDraftRun run) => run.Run;

    public RelationshipDraftRun WrapInterrupted(AnalysisRun run) => new(run, null);

    public void AddRun(CollectionAttemptDbContext db, RelationshipAnalysisRow row, RelationshipDraftRun run)
    {
        var runRow = AnalysisRunRows.Create<RelationshipAnalysisRunRow>(run.Run);
        runRow.Reason = run.Reason;
        db.Add(runRow);
    }

    public async Task<AnalysisSettlement<RelationshipDraftRun>> PrepareSettlementAsync(CollectionAttemptDbContext db,
        RelationshipAnalysisRow row, AnalysisSettlement<RelationshipDraftRun> settlement, CancellationToken cancellationToken)
    {
        if (settlement.Run is null && settlement.Checkpoint.ErrorCode is RelationshipDraftErrorCodes.Unchanged or
            RelationshipDraftErrorCodes.NoRelationship or RelationshipDraftErrorCodes.NoLongerMatches)
            throw new ArgumentException("A settlement without a revision needs its run.", nameof(settlement));
        if (settlement.Checkpoint is not RelationshipDraftCheckpoint { Revision: { } revision } checkpoint || settlement.Run is not { } run)
            return settlement;
        if (run.Run.Outcome is not (AnalysisRunOutcome.Drafted or AnalysisRunOutcome.Bypassed))
            throw new ArgumentException("A revision needs its drafted or bypassed run.", nameof(settlement));
        var refusal = await RelationshipDraftRows.AppendAnalysisRevisionAsync(db, revision, run.Run.RunId, cancellationToken);
        if (refusal is null) return settlement;
        var discarded = run.Run.Outcome == AnalysisRunOutcome.Drafted
            ? run with { Run = run.Run with { Outcome = AnalysisRunOutcome.DraftDiscarded } } : run;
        if (refusal is RelationshipDraftErrorCodes.AlreadyApproved or RelationshipDraftErrorCodes.PersonEditKept)
            return new(checkpoint with { ErrorCode = RelationshipDraftErrorCodes.Unchanged, Revision = null }, discarded);
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
