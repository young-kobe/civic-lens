using System.Data;
using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Analysis.Persistence;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Statements;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Analysis;

public sealed class PostgresDocumentChangeAnalysisStore : PostgresAnalysisStore<AnalysisRun>, IDocumentChangeAnalysisStore, IStaleAnalysisRequeue
{
    public const string Table = "document_change_analysis";

    private readonly IDbContextFactory<CollectionAttemptDbContext> contextFactory;
    private readonly PostgresAnalysisQueue<DocumentChangeAnalysisRow, DocumentChangeAnalysisBudgetRow, AnalysisRun> queue;
    private readonly AdmittingRevisionReader admittingRevisions = new();

    public PostgresDocumentChangeAnalysisStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
        : this(contextFactory, new PostgresAnalysisQueue<DocumentChangeAnalysisRow, DocumentChangeAnalysisBudgetRow, AnalysisRun>(
            contextFactory, new DocumentChangeAnalysisQueueMapping()))
    { }

    private PostgresDocumentChangeAnalysisStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory,
        PostgresAnalysisQueue<DocumentChangeAnalysisRow, DocumentChangeAnalysisBudgetRow, AnalysisRun> queue) : base(queue)
    {
        this.contextFactory = contextFactory;
        this.queue = queue;
    }

    public static PostgresDocumentChangeAnalysisStore FromConnectionString(string connectionString) =>
        new(AnalysisDbContextFactory.Create(connectionString));

    public async Task<IReadOnlyList<DocumentChangeAnalysisRecord>> GetByComparisonIdsAsync(
        IReadOnlyCollection<string> comparisonIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(comparisonIds);
        if (comparisonIds.Count > AnalysisPolicy.MaximumBatch) throw new ArgumentOutOfRangeException(nameof(comparisonIds));
        if (comparisonIds.Count == 0) return [];
        var ids = comparisonIds.ToArray();
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Set<DocumentChangeAnalysisRow>().AsNoTracking()
            .Where(row => row.Task == DocumentChangeDraftTask.Task && ids.Contains(row.ComparisonId)).ToArrayAsync(cancellationToken);
        return [.. rows.Select(ToRecord)];
    }

    /// <summary>Reads the run of the newest model revision of the draft.</summary>
    public async Task<AnalysisRun?> GetDraftRunAsync(string draftId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Set<DocumentChangeAnalysisRunRow>().AsNoTracking().Where(candidate => candidate.DraftId == draftId)
            .OrderByDescending(candidate => candidate.RevisionNumber).FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : AnalysisRunRows.ToRun(row);
    }

    public async Task<AnalysisRun?> GetRunAsync(string runId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Set<DocumentChangeAnalysisRunRow>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.RunId == runId, cancellationToken);
        return row is null ? null : AnalysisRunRows.ToRun(row);
    }

    public async Task<DocumentChangeDraftEvidence?> ReadEvidenceAsync(string comparisonId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(comparisonId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var verified = await PostgresDocumentChangeReviewStore.ReadVerifiedComparisonAsync(db, comparisonId, cancellationToken);
        var draftId = await PostgresDocumentChangeReviewStore.ReadDraftIdForComparisonAsync(db, comparisonId, cancellationToken);
        var stale = draftId is null ? null : (await StaleDocumentChangeDrafts.FindAsync(db, [comparisonId], cancellationToken)).GetValueOrDefault(comparisonId);
        var revision = verified is null ? null : await admittingRevisions.ReadForAttemptAsync(db, verified.After.SourceAttempt.AttemptId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return verified is null ? null : new(verified.Summary, verified.Before, verified.After, draftId, stale, revision);
    }

    public Task<int> RequeueStaleAsync(CancellationToken cancellationToken) =>
        queue.RequeueAsync(AnalysisState.Succeeded, null, null, async (db, settled, token) =>
        {
            var stale = await StaleDocumentChangeDrafts.FindAsync(db, [.. settled.Select(row => row.ComparisonId)], token);
            return settled.Where(row => stale.ContainsKey(row.ComparisonId));
        }, cancellationToken);

    private static DocumentChangeAnalysisRecord ToRecord(DocumentChangeAnalysisRow row) => new(row.ComparisonId, row.Task,
        row.Status, row.LeaseToken, row.Fence, row.Attempts,
        row.RetryAt is { } retry ? new DateTimeOffset(retry, TimeSpan.Zero) : null, row.DraftId, row.ErrorCode, row.RetryOfRunId);
}
