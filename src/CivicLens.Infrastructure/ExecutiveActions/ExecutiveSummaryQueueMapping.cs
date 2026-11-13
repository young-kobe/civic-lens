using CivicLens.Application.Analysis;
using CivicLens.Application.ExecutiveActions;
using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.ExecutiveActions.Persistence;

namespace CivicLens.Infrastructure.ExecutiveActions;

/// <summary>The table mapping of the summary queue. It follows <see cref="Actions.ActionDraftQueueMapping"/>, and it shares the budget table of the action queue,
/// as the brief check shares the table of the brief draft. The budget rows differ by their task.</summary>
internal sealed class ExecutiveSummaryQueueMapping(ExecutiveActionRows rows) : IAnalysisQueueMapping<ExecutiveSummaryAnalysisRow, ExecutiveSummaryRun>
{
    public string Table => PostgresExecutiveSummaryStore.Table;

    public string KeyColumn => "record_id";

    public string KeyProperty => nameof(ExecutiveSummaryAnalysisRow.RecordId);

    public string BudgetTable => "action_analysis_budget";

    public string? BudgetTask => ExecutiveSummaryTask.Task;

    public AnalysisTaskIdentity Identity => ExecutiveSummaryTask.Identity;

    public bool CanTransition(AnalysisRecord record, AnalysisCheckpoint checkpoint) => ExecutiveSummaryLifecycle.CanTransition(record, checkpoint);

    public AnalysisRun RunOf(ExecutiveSummaryRun run) => run.Run;

    public ExecutiveSummaryRun WrapInterrupted(AnalysisRun run) => new(run, null);

    public void AddRun(CollectionAttemptDbContext db, ExecutiveSummaryAnalysisRow row, ExecutiveSummaryRun run)
    {
        var runRow = AnalysisRunRows.Create<ExecutiveSummaryAnalysisRunRow>(run.Run);
        runRow.Reason = run.Reason;
        db.Add(runRow);
    }

    /// <summary>A drafted run appends its revision under the executive action review lock. A refusal discards the run, and a conflict retries.</summary>
    public async Task<AnalysisSettlement<ExecutiveSummaryRun>> PrepareSettlementAsync(CollectionAttemptDbContext db, ExecutiveSummaryAnalysisRow row,
        AnalysisSettlement<ExecutiveSummaryRun> settlement, CancellationToken cancellationToken)
    {
        if (settlement.Checkpoint is not ExecutiveSummaryCheckpoint { Revision: { } revision } checkpoint) return settlement;
        if (settlement.Run is not { Run.Outcome: AnalysisRunOutcome.Drafted } run)
            throw new ArgumentException("A summary revision needs its drafted run.", nameof(settlement));
        var refusal = await rows.AppendAnalysisRevisionAsync(db, revision, run.Run.RunId, cancellationToken);
        if (refusal is null) return settlement;
        var discarded = run with { Run = run.Run with { Outcome = AnalysisRunOutcome.DraftDiscarded }, Reason = refusal };
        if (refusal == ExecutiveSummaryErrorCodes.RecordRejected) return new(checkpoint with { ErrorCode = refusal, Revision = null }, discarded);
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
