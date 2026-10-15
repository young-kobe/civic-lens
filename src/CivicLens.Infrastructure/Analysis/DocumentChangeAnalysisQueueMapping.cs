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

    public void AddRun(CollectionAttemptDbContext db, DocumentChangeAnalysisRow row, AnalysisRun run) => db.Add(ToRow(run, row.DraftId));

    public async Task<AnalysisSettlement<AnalysisRun>> PrepareSettlementAsync(CollectionAttemptDbContext db,
        DocumentChangeAnalysisRow row, AnalysisSettlement<AnalysisRun> settlement, CancellationToken cancellationToken)
    {
        if (settlement.Checkpoint is not DocumentChangeDraftCheckpoint { Revision: { } revision } checkpoint) return settlement;
        if (settlement.Run?.Outcome != AnalysisRunOutcome.Drafted)
            throw new ArgumentException("A draft needs its drafted run.", nameof(settlement));
        if (await PostgresDocumentChangeReviewStore.TryAddAnalysisDraftAsync(db, revision, cancellationToken))
        {
            row.DraftId = revision.DraftId;
            return settlement;
        }
        return new(checkpoint with { Revision = null, ErrorCode = DocumentChangeDraftErrorCodes.DraftDiscarded },
            settlement.Run with { Outcome = AnalysisRunOutcome.DraftDiscarded });
    }

    public static DocumentChangeAnalysisRunRow ToRow(AnalysisRun run, string? draftId)
    {
        var row = AnalysisRunRows.Create<DocumentChangeAnalysisRunRow>(run);
        row.DraftId = draftId;
        row.RevisionNumber = draftId is null ? null : 1;
        return row;
    }
}
