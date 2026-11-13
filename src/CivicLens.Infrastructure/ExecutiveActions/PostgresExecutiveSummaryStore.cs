using System.Linq.Expressions;
using CivicLens.Application;
using CivicLens.Application.Analysis;
using CivicLens.Application.ExecutiveActions;
using CivicLens.Core.Analysis;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Actions.Persistence;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents.Persistence;
using CivicLens.Infrastructure.ExecutiveActions.Persistence;
using Microsoft.EntityFrameworkCore;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Infrastructure.ExecutiveActions;

public sealed class PostgresExecutiveSummaryStore : PostgresAnalysisStore<ExecutiveSummaryRun>, IExecutiveSummaryStore
{
    public const string Table = "exec_summary_analysis";

    private readonly IDbContextFactory<CollectionAttemptDbContext> contextFactory;

    public PostgresExecutiveSummaryStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory, CodeVersion codeVersion)
        : base(new PostgresAnalysisQueue<ExecutiveSummaryAnalysisRow, ActionAnalysisBudgetRow, ExecutiveSummaryRun>(contextFactory,
            new ExecutiveSummaryQueueMapping(new ExecutiveActionRows(codeVersion))))
    {
        this.contextFactory = contextFactory;
    }

    public async Task<ExecutiveSummaryRun?> GetRunAsync(string runId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Set<ExecutiveSummaryAnalysisRunRow>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.RunId == runId && candidate.Task == ExecutiveSummaryTask.Task, cancellationToken);
        return row is null ? null : new(AnalysisRunRows.ToRun(row), row.Reason);
    }

    public async Task<ExecutiveSummaryEvidence?> ReadEvidenceAsync(string recordId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var draft = await db.Set<ExecutiveActionDraftRow>().AsNoTracking().SingleOrDefaultAsync(row => row.RecordId == recordId, cancellationToken);
        if (draft is null) return null;
        var current = await ExecutiveActionRows.ForReading.Records.FindRevisionAsync(db, recordId, draft.CurrentRevisionNumber, cancellationToken);
        var text = await ExecutiveSummaryTexts.ReadAsync(db, recordId, cancellationToken);
        return current is null || text is null || text.BodyStart < 0 ? null : new(current, text, draft.ReviewStatus == ReviewStatus.Rejected);
    }

    public async Task<string?> FindRecordIdAsync(string documentNumber, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Set<ExecutiveActionDocumentRow>().AsNoTracking()
            .Where(row => row.DocumentNumber == documentNumber && row.NoticeUrl == null).Select(row => row.RecordId).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExecutiveSummaryListItem>> ListAsync(ExecutiveSummaryView? view, int limit, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var queue = db.Set<ExecutiveSummaryAnalysisRow>().AsNoTracking().Where(row => row.Task == ExecutiveSummaryTask.Task);
        if (view is { } selected) queue = queue.Where(InView(selected));
        var rows = await (from entry in queue
                          join document in db.Set<ExecutiveActionDocumentRow>().AsNoTracking() on entry.RecordId equals document.RecordId
                          where document.NoticeUrl == null
                          orderby entry.CreatedAtUtcTicks, entry.RecordId
                          select new { Entry = entry, document.DocumentNumber }).Take(limit).ToArrayAsync(cancellationToken);
        var ids = rows.Select(row => row.Entry.RecordId).ToArray();
        var latest = await AnalysisRunLookup.Latest<ExecutiveSummaryAnalysisRunRow>(db, ExecutiveSummaryTask.Task, nameof(ExecutiveSummaryAnalysisRunRow.RecordId), ids)
            .Select(run => new { run.RecordId, run.RunId }).ToDictionaryAsync(run => run.RecordId, run => run.RunId, cancellationToken);
        return [.. rows.Select(row => new ExecutiveSummaryListItem(row.Entry.RecordId, row.DocumentNumber, row.Entry.Status,
            ViewOf(row.Entry.Status, row.Entry.ErrorCode), row.Entry.ErrorCode, row.Entry.Attempts, latest.GetValueOrDefault(row.Entry.RecordId)))];
    }

    public async Task<IReadOnlyDictionary<ExecutiveSummaryView, int>> CountAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await AnalysisQueueCounts.CountAsync<ExecutiveSummaryAnalysisRow, ExecutiveSummaryView>(db, ExecutiveSummaryTask.Task, ViewOf, cancellationToken);
    }

    private static readonly (ExecutiveSummaryView View, string? ErrorCode)[] SucceededViews =
    [
        (ExecutiveSummaryView.Drafted, null),
        (ExecutiveSummaryView.Unchanged, ExecutiveSummaryErrorCodes.Unchanged),
        (ExecutiveSummaryView.RecordRejected, ExecutiveSummaryErrorCodes.RecordRejected),
    ];

    private static Expression<Func<ExecutiveSummaryAnalysisRow, bool>> InView(ExecutiveSummaryView view) => view switch
    {
        ExecutiveSummaryView.Pending => AnalysisQueueStates.IsPending<ExecutiveSummaryAnalysisRow>(),
        ExecutiveSummaryView.Failed => AnalysisQueueStates.IsFailed<ExecutiveSummaryAnalysisRow>(),
        _ => AnalysisQueueStates.IsSucceeded<ExecutiveSummaryAnalysisRow>(SucceededViews.Single(item => item.View == view).ErrorCode)
    };

    private static ExecutiveSummaryView ViewOf(State status, string? errorCode) =>
        status == State.Succeeded ? SucceededViews.Single(item => item.ErrorCode == errorCode).View
            : AnalysisQueueStates.IsFailed(status) ? ExecutiveSummaryView.Failed : ExecutiveSummaryView.Pending;
}
