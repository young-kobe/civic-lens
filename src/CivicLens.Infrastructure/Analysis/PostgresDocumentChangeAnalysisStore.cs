using System.Data;
using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Analysis.Persistence;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Analysis;

public sealed class PostgresDocumentChangeAnalysisStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
    : PostgresAnalysisStore<AnalysisRun>(new PostgresAnalysisQueue<DocumentChangeAnalysisRow, DocumentChangeAnalysisBudgetRow, AnalysisRun>(
        contextFactory, new DocumentChangeAnalysisQueueMapping())), IDocumentChangeAnalysisStore
{
    public const string Table = "document_change_analysis";

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
            .Where(row => row.Task == DocumentChangeDraftingTask.Task && ids.Contains(row.ComparisonId)).ToArrayAsync(cancellationToken);
        return [.. rows.Select(ToRecord)];
    }

    public async Task<AnalysisRun?> GetDraftRunAsync(string draftId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Set<DocumentChangeAnalysisRunRow>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.DraftId == draftId, cancellationToken);
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

    public async Task<DocumentChangeAnalysisEvidence?> ReadEvidenceAsync(string comparisonId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(comparisonId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var verified = await PostgresDocumentChangeReviewStore.ReadVerifiedComparisonAsync(db, comparisonId, cancellationToken);
        var draftId = await PostgresDocumentChangeReviewStore.ReadDraftIdForComparisonAsync(db, comparisonId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return verified is null ? null : new(verified.Summary, verified.Before, verified.After, draftId);
    }

    private static DocumentChangeAnalysisRecord ToRecord(DocumentChangeAnalysisRow row) => new(row.ComparisonId, row.Task,
        row.Status, row.LeaseToken, row.Fence, row.Attempts,
        row.RetryAt is { } retry ? new DateTimeOffset(retry, TimeSpan.Zero) : null, row.DraftId, row.ErrorCode, row.RetryOfRunId);
}
