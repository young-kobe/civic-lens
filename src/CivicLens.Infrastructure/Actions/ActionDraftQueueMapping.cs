using System.Linq.Expressions;
using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Actions.Persistence;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.BillStream;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Actions;

internal sealed class ActionDraftQueueMapping : IAnalysisQueueMapping<ActionAnalysisRow, ActionDraftRun>
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

    public Expression<Func<ActionAnalysisRow, bool>> ClaimedFirst(CollectionAttemptDbContext db) =>
        row => db.Set<ActionCandidateRow>().Any(candidate => candidate.CandidateId == row.CandidateId && candidate.IncludesSummaries);

    public async Task<AnalysisSettlement<ActionDraftRun>> PrepareSettlementAsync(CollectionAttemptDbContext db, ActionAnalysisRow row,
        AnalysisSettlement<ActionDraftRun> settlement, CancellationToken cancellationToken)
    {
        if (settlement.Run is null && settlement.Checkpoint.ErrorCode is ActionDraftErrorCodes.Unchanged or ActionDraftErrorCodes.NoTrackedOfficial)
            throw new ArgumentException("A bypassed settlement needs its run.", nameof(settlement));
        if (settlement.Checkpoint is not ActionDraftCheckpoint { Revision: { } revision } checkpoint || settlement.Run is not { } run)
            return settlement;
        if (run.Run.Outcome != AnalysisRunOutcome.Drafted) throw new ArgumentException("A revision needs its drafted run.", nameof(settlement));
        if (await ActionDraftRows.TryAppendAnalysisRevisionAsync(db, revision, run.Run.RunId, cancellationToken)) return settlement;
        row.RetryOfRunId = null;
        return new(checkpoint with
        {
            Status = AnalysisState.RetryWaiting,
            ErrorCode = ActionDraftErrorCodes.RevisionConflict,
            RetryDelay = AnalysisPolicy.RetryDelay(row.Attempts),
            Revision = null
        }, run with { Run = run.Run with { Outcome = AnalysisRunOutcome.DraftDiscarded } });
    }
}
