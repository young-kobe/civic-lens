using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Statements.Persistence;

namespace CivicLens.Infrastructure.Statements;

internal sealed class StatementDraftQueueMapping : IAnalysisQueueMapping<StatementAnalysisRow, StatementDraftRun>
{
    public string Table => PostgresStatementGateStore.Table;

    public string KeyColumn => "candidate_id";

    public string KeyProperty => nameof(StatementAnalysisRow.CandidateId);

    public string BudgetTable => "statement_analysis_budget";

    public string? BudgetTask => StatementDraftTask.Task;

    public AnalysisTaskIdentity Identity => StatementDraftTask.Identity;

    public bool CanTransition(AnalysisRecord record, AnalysisCheckpoint checkpoint) => StatementDraftLifecycle.CanTransition(record, checkpoint);

    public AnalysisRun RunOf(StatementDraftRun run) => run.Run;

    public StatementDraftRun WrapInterrupted(AnalysisRun run) => new(run, null);

    public void AddRun(CollectionAttemptDbContext db, StatementAnalysisRow row, StatementDraftRun run)
    {
        var runRow = AnalysisRunRows.Create<StatementAnalysisRunRow>(run.Run);
        runRow.Reason = run.Reason;
        db.Add(runRow);
    }

    public async Task<AnalysisSettlement<StatementDraftRun>> PrepareSettlementAsync(CollectionAttemptDbContext db, StatementAnalysisRow row,
        AnalysisSettlement<StatementDraftRun> settlement, CancellationToken cancellationToken)
    {
        if (settlement.Checkpoint is not StatementDraftCheckpoint { Revision: { } revision } checkpoint || settlement.Run is not { } run)
            return settlement;
        if (run.Run.Outcome != AnalysisRunOutcome.Drafted) throw new ArgumentException("A draft needs its drafted run.", nameof(settlement));
        if (await StatementDraftRows.TryAddAnalysisDraftAsync(db, revision, run.Run.RunId, cancellationToken)) return settlement;
        return new(checkpoint with { Revision = null, ErrorCode = StatementDraftErrorCodes.DraftDiscarded },
            run with { Run = run.Run with { Outcome = AnalysisRunOutcome.DraftDiscarded } });
    }

    public async Task AfterTransitionAsync(CollectionAttemptDbContext db, StatementAnalysisRow row, AnalysisCheckpoint checkpoint, long now,
        CancellationToken cancellationToken)
    {
        if (checkpoint.Status == AnalysisState.Succeeded && checkpoint.ErrorCode == StatementDraftErrorCodes.OverByteCap)
            await StatementDraftRows.AddDraftWithoutRevisionAsync(db, row.CandidateId, now, cancellationToken);
    }
}
