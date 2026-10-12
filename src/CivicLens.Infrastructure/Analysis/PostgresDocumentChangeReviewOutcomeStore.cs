using System.Data;
using CivicLens.Application.Analysis;
using CivicLens.Application.Paging;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Analysis.Persistence;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Review.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Infrastructure.Analysis;

/// <summary>Reads AI drafts with their review history in keyset pages. Each page costs four queries, whatever its size.</summary>
public sealed class PostgresDocumentChangeReviewOutcomeStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
    : IDocumentChangeReviewOutcomeStore
{
    public static PostgresDocumentChangeReviewOutcomeStore FromConnectionString(string connectionString)
    {
        _ = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
    }

    public async Task<KeysetSlice<AiDraftOutcome>> ListAiDraftsAsync(PageCursor? cursor, int limit,
        CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var aiDrafts = db.Set<DocumentChangeDraftRow>().AsNoTracking()
            .Where(draft => db.Set<DocumentChangeAnalysisRunRow>().Any(run => run.DraftId == draft.DraftId));
        var fetched = await PostgresDocumentChangeReviewStore.KeysetDrafts(aiDrafts, cursor).Take(limit + 1).ToArrayAsync(cancellationToken);
        var slice = KeysetSlice<DocumentChangeDraftRow>.Create(fetched, limit, cursor, row => (row.CreatedAtUtcTicks, row.DraftId));
        var ids = slice.Items.Select(row => row.DraftId).ToArray();
        var promptVersions = await db.Set<DocumentChangeAnalysisRunRow>().AsNoTracking()
            .Where(run => ids.Contains(run.DraftId!)).ToDictionaryAsync(run => run.DraftId!, run => run.PromptVersion,
                StringComparer.Ordinal, cancellationToken);
        var revisions = await PostgresDocumentChangeReviewStore.ReadRevisionRowsAsync(db, ids, cancellationToken);
        var decisions = await PostgresDocumentChangeReviewStore.ReadDecisionRowsAsync(db, ids, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var items = slice.Items.Select(row => ToOutcome(row, promptVersions[row.DraftId], revisions[row.DraftId], decisions[row.DraftId]));
        return new([.. items], slice.NewerCursor, slice.OlderCursor);
    }

    private static AiDraftOutcome ToOutcome(DocumentChangeDraftRow draft, string promptVersion,
        IEnumerable<DocumentChangeRevisionRow> revisions, IEnumerable<ReviewDecisionRow> decisions)
    {
        var (first, current) = PostgresDocumentChangeReviewStore.ToValidatedListItem(draft, revisions, decisions);
        if (!ReviewAuthor.IsAnalysis(first.AuthorSubject))
            throw new InvalidOperationException("An analysis run is linked to a draft that the model did not write.");
        return new(promptVersion, first, current);
    }
}
