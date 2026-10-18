using CivicLens.Application.Analysis;
using CivicLens.Application.Briefs;
using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Briefs.Persistence;
using CivicLens.Infrastructure.Collection;

namespace CivicLens.Infrastructure.Briefs;

internal sealed class BriefDraftQueueMapping : IAnalysisQueueMapping<BriefAnalysisRow, BriefDraftRun>
{
    public string Table => PostgresBriefDraftStore.Table;

    public string KeyColumn => "record_id";

    public string KeyProperty => nameof(BriefAnalysisRow.RecordId);

    public string BudgetTable => "brief_analysis_budget";

    public string? BudgetTask => BriefDraftTask.Task;

    public AnalysisTaskIdentity Identity => BriefDraftTask.Identity;

    public bool CanTransition(AnalysisRecord record, AnalysisCheckpoint checkpoint) => BriefDraftLifecycle.CanTransition(record, checkpoint);

    public AnalysisRun RunOf(BriefDraftRun run) => run.Run;

    public BriefDraftRun WrapInterrupted(AnalysisRun run) => new(run, null);

    public void AddRun(CollectionAttemptDbContext db, BriefAnalysisRow row, BriefDraftRun run)
    {
        var runRow = AnalysisRunRows.Create<BriefAnalysisRunRow>(run.Run);
        runRow.Reason = run.Reason;
        db.Add(runRow);
    }

    public async Task<AnalysisSettlement<BriefDraftRun>> PrepareSettlementAsync(CollectionAttemptDbContext db, BriefAnalysisRow row,
        AnalysisSettlement<BriefDraftRun> settlement, CancellationToken cancellationToken)
    {
        if (settlement.Run is null && settlement.Checkpoint.ErrorCode is BriefDraftErrorCodes.Unchanged or BriefDraftErrorCodes.NoNewRecords)
            throw new ArgumentException("A bypassed settlement needs its run.", nameof(settlement));
        if (settlement.Checkpoint is not BriefDraftCheckpoint { Revision: { } revision } checkpoint || settlement.Run is not { } run)
            return settlement;
        if (run.Run.Outcome != AnalysisRunOutcome.Drafted)
            throw new ArgumentException("A revision needs its drafted run.", nameof(settlement));
        var refusal = await BriefDraftRows.AppendAnalysisRevisionAsync(db, revision, run.Run.RunId, cancellationToken);
        if (refusal is null) return settlement;
        var discarded = run with { Run = run.Run with { Outcome = AnalysisRunOutcome.DraftDiscarded } };
        if (refusal == BriefDraftErrorCodes.AlreadyApproved)
            return new(checkpoint with { ErrorCode = BriefDraftErrorCodes.Unchanged, Revision = null }, discarded);
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
