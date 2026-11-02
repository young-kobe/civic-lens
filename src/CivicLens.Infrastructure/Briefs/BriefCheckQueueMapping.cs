using CivicLens.Application.Analysis;
using CivicLens.Application.Briefs;
using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Briefs.Persistence;
using CivicLens.Infrastructure.Collection;

namespace CivicLens.Infrastructure.Briefs;

internal sealed class BriefCheckQueueMapping : IAnalysisQueueMapping<BriefAnalysisRow, BriefCheckRun>
{
    public string Table => PostgresBriefDraftStore.Table;

    public string KeyColumn => "record_id";

    public string KeyProperty => nameof(BriefAnalysisRow.RecordId);

    public string BudgetTable => "brief_analysis_budget";

    public string? BudgetTask => BriefCheckTask.Task;

    public AnalysisTaskIdentity Identity => BriefCheckTask.Identity;

    public bool CanTransition(AnalysisRecord record, AnalysisCheckpoint checkpoint) => BriefCheckLifecycle.CanTransition(record, checkpoint);

    public AnalysisRun RunOf(BriefCheckRun run) => run.Run;

    public BriefCheckRun WrapInterrupted(AnalysisRun run) => new(run, null, null, null, null);

    public void AddRun(CollectionAttemptDbContext db, BriefAnalysisRow row, BriefCheckRun run)
    {
        var runRow = AnalysisRunRows.Create<BriefAnalysisRunRow>(run.Run);
        runRow.Reason = run.Reason;
        runRow.SourceRunId = run.SourceRunId;
        runRow.InputFingerprint = run.InputFingerprint;
        if (run.Result is { } result)
        {
            runRow.CheckedRevisionNumber = result.RevisionNumber;
            runRow.Recommendation = result.Recommendation;
            db.AddRange(result.Flags.Select((flag, index) => ToRow(run.Run.RunId, index + 1, flag)));
        }
        db.Add(runRow);
    }

    public Task<AnalysisSettlement<BriefCheckRun>> PrepareSettlementAsync(CollectionAttemptDbContext db, BriefAnalysisRow row,
        AnalysisSettlement<BriefCheckRun> settlement, CancellationToken cancellationToken)
    {
        if (settlement.Run is null && settlement.Checkpoint.ErrorCode is BriefDraftErrorCodes.Unchanged)
            throw new ArgumentException("A bypassed settlement needs its run.", nameof(settlement));
        return Task.FromResult(settlement);
    }

    private static BriefCheckFlagRow ToRow(string runId, int flagNumber, BriefCheckFlag flag) => new()
    {
        RunId = runId,
        FlagNumber = flagNumber,
        ParagraphIndex = flag.ParagraphIndex,
        Code = flag.Code,
        CitationId = flag.CitationId,
        Message = flag.Note
    };
}
