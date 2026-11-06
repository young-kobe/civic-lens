using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Analysis.Persistence;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;

namespace CivicLens.Infrastructure.Analysis;

internal sealed class DocumentChangeAnalysisQueueMapping : IAnalysisQueueMapping<DocumentChangeAnalysisRow, AnalysisRun>
{
    public string Table => PostgresDocumentChangeAnalysisStore.Table;

    public string KeyColumn => "comparison_id";

    public string KeyProperty => nameof(DocumentChangeAnalysisRow.ComparisonId);

    public string BudgetTable => "document_change_analysis_budget";

    public string? BudgetTask => null;

    public AnalysisTaskIdentity Identity => DocumentChangeDraftTask.Identity;

    public bool CanTransition(AnalysisRecord record, AnalysisCheckpoint checkpoint) =>
        DocumentChangeDraftLifecycle.CanTransition(record, checkpoint);

    public AnalysisRun RunOf(AnalysisRun run) => run;

    public AnalysisRun WrapInterrupted(AnalysisRun run) => run;

    /// <summary>An analysis run links to the revision that this settlement added. A run that added none links to no revision.</summary>
    public void AddRun(CollectionAttemptDbContext db, DocumentChangeAnalysisRow row, AnalysisRun run) => db.Add(ToRow(run, row.AppendedRevision));

    public async Task<AnalysisSettlement<AnalysisRun>> PrepareSettlementAsync(CollectionAttemptDbContext db,
        DocumentChangeAnalysisRow row, AnalysisSettlement<AnalysisRun> settlement, CancellationToken cancellationToken)
    {
        if (settlement.Checkpoint is not DocumentChangeDraftCheckpoint { Revision: { } revision } checkpoint) return settlement;
        if (settlement.Run?.Outcome != AnalysisRunOutcome.Drafted)
            throw new ArgumentException("A draft needs its drafted run.", nameof(settlement));
        var refusal = await PostgresDocumentChangeReviewStore.AppendAnalysisRevisionAsync(db, revision, cancellationToken);
        if (refusal is null)
        {
            row.DraftId = revision.DraftId;
            row.AppendedRevision = (revision.DraftId, revision.RevisionNumber);
            return settlement;
        }
        var discarded = settlement.Run with { Outcome = AnalysisRunOutcome.DraftDiscarded };
        if (refusal == DocumentChangeDraftErrorCodes.DraftDiscarded)
            return new(checkpoint with { Revision = null, ErrorCode = refusal }, discarded);
        row.RetryOfRunId = null;
        return new(checkpoint with
        {
            Status = AnalysisState.RetryWaiting,
            ErrorCode = refusal,
            RetryDelay = AnalysisPolicy.RetryDelay(row.Attempts),
            Revision = null
        }, discarded);
    }

    private static DocumentChangeAnalysisRunRow ToRow(AnalysisRun run, (string DraftId, int RevisionNumber)? revision)
    {
        var row = AnalysisRunRows.Create<DocumentChangeAnalysisRunRow>(run);
        row.DraftId = revision?.DraftId;
        row.RevisionNumber = revision?.RevisionNumber;
        return row;
    }
}
