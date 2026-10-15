using System.Collections.Immutable;
using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Review;

internal sealed class ReviewRecords<TDraftRow, TRevisionRow, TDecisionRow, TRevision>(string lockKey, string draftName,
    string recordIdProperty)
    where TDraftRow : class, IReviewDraftRow
    where TRevisionRow : class, IReviewRevisionRow
    where TDecisionRow : class, IReviewDecisionRow
    where TRevision : class, IReviewRevision
{
    public async Task<TRevision> CreateAsync(IDbContextFactory<CollectionAttemptDbContext> contextFactory, string actorSubject,
        TRevision revision, string operation, string idempotencyKey, string payloadHash,
        Func<CollectionAttemptDbContext, Task<TRevision>> create, CancellationToken cancellationToken)
    {
        ReviewIdempotency.ValidateRequest(actorSubject, idempotencyKey, payloadHash);
        ArgumentNullException.ThrowIfNull(revision);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await ReviewIdempotency.WriteOnceAsync(db, lockKey, actorSubject, operation, idempotencyKey, payloadHash, async () =>
        {
            if (revision.RevisionNumber != 1) throw new InvalidOperationException("A draft must begin at revision one.");
            ReviewWriteRules.RequireRevisionAuthor(revision.AuthorSubject, actorSubject);
            return await create(db);
        }, cancellationToken);
    }

    public async Task<TRevision> SaveRevisionAsync(IDbContextFactory<CollectionAttemptDbContext> contextFactory, string actorSubject,
        TRevision revision, int expectedRevisionNumber, string operation, string idempotencyKey, string payloadHash,
        Func<CollectionAttemptDbContext, TRevision, Task<TRevision>> prepare, Func<TRevision, TRevisionRow> toRow,
        CancellationToken cancellationToken)
    {
        ReviewIdempotency.ValidateRequest(actorSubject, idempotencyKey, payloadHash);
        ArgumentNullException.ThrowIfNull(revision);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await ReviewIdempotency.WriteOnceAsync(db, lockKey, actorSubject, operation, idempotencyKey, payloadHash, async () =>
        {
            var draft = await ReadDraftForWriteAsync(db, revision.RecordId, cancellationToken);
            ReviewWriteRules.RequireSaveable(draft.CurrentRevisionNumber, expectedRevisionNumber, revision.RevisionNumber);
            ReviewWriteRules.RequireRevisionAuthor(revision.AuthorSubject, actorSubject);
            var current = await ReadRevisionAsync(db, draft.RecordId, draft.CurrentRevisionNumber, cancellationToken);
            var saved = await prepare(db, current);
            db.Add(toRow(saved));
            draft.CurrentRevisionNumber = saved.RevisionNumber;
            var decisionRows = await ReadDecisionRowsAsync(db, [draft.RecordId], cancellationToken);
            draft.ReviewStatus = ReviewPolicy.GetCurrentStatus(draft.CurrentRevisionNumber,
                ReviewDecisionHistory.Parse(draft.ReviewStateVersion, decisionRows[draft.RecordId]));
            return saved;
        }, cancellationToken);
    }

    public async Task<ReviewDecision> DecideAsync(IDbContextFactory<CollectionAttemptDbContext> contextFactory, string actorSubject,
        ReviewDecision decision, int expectedRevisionNumber, int expectedReviewStateVersion, string operation, string idempotencyKey,
        string payloadHash, Func<CollectionAttemptDbContext, TDraftRow, ReviewHistory<TRevision>, Task> validateDecision,
        Func<ReviewDecision, TDecisionRow> toRow, CancellationToken cancellationToken)
    {
        ReviewIdempotency.ValidateRequest(actorSubject, idempotencyKey, payloadHash);
        ReviewWriteRules.RequireDecisionActor(decision, actorSubject);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await ReviewIdempotency.WriteOnceAsync(db, lockKey, actorSubject, operation, idempotencyKey, payloadHash, async () =>
        {
            var draft = await ReadDraftForWriteAsync(db, decision.DraftId, cancellationToken);
            ReviewWriteRules.RequireDecidable(draft.CurrentRevisionNumber, draft.ReviewStateVersion, expectedRevisionNumber,
                expectedReviewStateVersion, decision);
            var history = (await ReadHistoriesAsync(db, [draft], cancellationToken))[draft.RecordId];
            ReviewPolicy.ValidateDecision(draft.RecordId, draft.CurrentRevisionNumber, draft.ReviewStateVersion, history.Decisions, decision);
            await validateDecision(db, draft, history);
            db.Add(toRow(decision));
            draft.ReviewStateVersion = decision.ReviewStateVersion;
            draft.ReviewStatus = ReviewPolicy.GetCurrentStatus(draft.CurrentRevisionNumber, history.Decisions.Add(decision));
            return decision;
        }, cancellationToken);
    }

    public IQueryable<TDraftRow> Drafts(CollectionAttemptDbContext db, DraftStatusFilter filter)
    {
        var drafts = db.Set<TDraftRow>().AsNoTracking();
        return filter switch
        {
            DraftStatusFilter.All => drafts,
            DraftStatusFilter.NeedsAction => drafts.Where(draft => draft.ReviewStatus != ReviewStatus.Approved),
            DraftStatusFilter.Approved => drafts.Where(draft => draft.ReviewStatus == ReviewStatus.Approved),
            _ => throw new ArgumentOutOfRangeException(nameof(filter))
        };
    }

    public async Task<KeysetSlice<TDraftRow>> PageAsync(CollectionAttemptDbContext db, PageCursor? cursor, int limit,
        DraftStatusFilter filter, CancellationToken cancellationToken)
    {
        var fetched = await ReviewKeyset.Page(Drafts(db, filter), cursor, row => row.CreatedAtUtcTicks,
            row => EF.Property<string>(row, recordIdProperty)).Take(limit + 1).ToArrayAsync(cancellationToken);
        return KeysetSlice<TDraftRow>.Create(fetched, limit, cursor, row => (row.CreatedAtUtcTicks, row.RecordId));
    }

    public Task<TDraftRow[]> ReadDraftsAsync(CollectionAttemptDbContext db, string[] recordIds, CancellationToken cancellationToken) =>
        db.Set<TDraftRow>().AsNoTracking().Where(row => recordIds.Contains(EF.Property<string>(row, recordIdProperty)))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<(TDraftRow Draft, ReviewHistory<TRevision> History, int? PublishedRevisionNumber)>>
        ReadUnpublishedApprovedAsync(CollectionAttemptDbContext db, string unpublishedApprovedSql, int limit,
            CancellationToken cancellationToken)
    {
        var keys = await db.Database.SqlQueryRaw<UnpublishedKey>(unpublishedApprovedSql)
            .Where(key => key.PublishedRevisionNumber == null || key.PublishedRevisionNumber != key.CurrentRevisionNumber)
            .OrderByDescending(key => key.CreatedAtUtcTicks).ThenByDescending(key => key.RecordId)
            .Take(limit).ToArrayAsync(cancellationToken);
        var drafts = (await ReadDraftsAsync(db, [.. keys.Select(key => key.RecordId)], cancellationToken))
            .ToDictionary(row => row.RecordId, StringComparer.Ordinal);
        var histories = await ReadHistoriesAsync(db, drafts.Values, cancellationToken);
        return [.. keys.Select(key => (drafts[key.RecordId], histories[key.RecordId], key.PublishedRevisionNumber))];
    }

    public async Task<Dictionary<string, ReviewHistory<TRevision>>> ReadHistoriesAsync(CollectionAttemptDbContext db,
        IReadOnlyCollection<TDraftRow> drafts, CancellationToken cancellationToken)
    {
        if (drafts.Count == 0) return [];
        var recordIds = drafts.Select(draft => draft.RecordId).ToArray();
        var revisions = await ReadRevisionRowsAsync(db, recordIds, cancellationToken);
        var decisions = await ReadDecisionRowsAsync(db, recordIds, cancellationToken);
        return drafts.ToDictionary(draft => draft.RecordId,
            draft => BuildHistory(draft, revisions[draft.RecordId], decisions[draft.RecordId]), StringComparer.Ordinal);
    }

    public async Task<ILookup<string, TRevisionRow>> ReadRevisionRowsAsync(CollectionAttemptDbContext db, string[] recordIds,
        CancellationToken cancellationToken)
    {
        var rows = await db.Set<TRevisionRow>().AsNoTracking().Where(row => recordIds.Contains(EF.Property<string>(row, recordIdProperty)))
            .OrderBy(row => EF.Property<string>(row, recordIdProperty)).ThenBy(row => row.RevisionNumber)
            .Take(recordIds.Length * (ReviewPolicy.MaximumRevisionsPerRecord + 1)).ToArrayAsync(cancellationToken);
        return rows.ToLookup(row => row.RecordId, StringComparer.Ordinal);
    }

    public async Task<ILookup<string, TDecisionRow>> ReadDecisionRowsAsync(CollectionAttemptDbContext db, string[] recordIds,
        CancellationToken cancellationToken)
    {
        var rows = await db.Set<TDecisionRow>().AsNoTracking().Where(row => recordIds.Contains(EF.Property<string>(row, recordIdProperty)))
            .OrderBy(row => EF.Property<string>(row, recordIdProperty)).ThenBy(row => row.ReviewStateVersion)
            .Take(recordIds.Length * (ReviewPolicy.MaximumDecisionsPerRecord + 1)).ToArrayAsync(cancellationToken);
        return rows.ToLookup(row => row.RecordId, StringComparer.Ordinal);
    }

    public ReviewHistory<TRevision> BuildHistory(TDraftRow draft, IEnumerable<TRevisionRow> revisionRows,
        IEnumerable<TDecisionRow> decisionRows)
    {
        var revisions = ParseRevisions(draft, revisionRows);
        var decisions = ReviewDecisionHistory.Parse(draft.ReviewStateVersion, decisionRows);
        ReviewDecisionHistory.Validate(draft.RecordId, revisions, decisions);
        var concerns = ReviewPolicy.GetUnresolvedConcerns(decisions);
        return new(revisions, decisions, concerns, ReviewPolicy.GetCurrentStatus(draft.CurrentRevisionNumber, decisions, concerns));
    }

    private async Task<TDraftRow> ReadDraftForWriteAsync(CollectionAttemptDbContext db, string recordId,
        CancellationToken cancellationToken) =>
        await db.Set<TDraftRow>().SingleOrDefaultAsync(row => EF.Property<string>(row, recordIdProperty) == recordId, cancellationToken)
            ?? throw new KeyNotFoundException($"{draftName} draft was not found.");

    private async Task<TRevision> ReadRevisionAsync(CollectionAttemptDbContext db, string recordId, int revisionNumber,
        CancellationToken cancellationToken)
    {
        var row = await db.Set<TRevisionRow>().AsNoTracking().SingleOrDefaultAsync(candidate =>
                EF.Property<string>(candidate, recordIdProperty) == recordId && candidate.RevisionNumber == revisionNumber, cancellationToken)
            ?? throw new InvalidOperationException("Current draft revision is missing.");
        var revision = ReviewJson.Deserialize<TRevision>(row.RevisionJson, "revision");
        if (revision.RecordId != recordId || revision.RevisionNumber != revisionNumber)
            throw new InvalidOperationException("Stored revision identity is inconsistent.");
        return revision;
    }

    private static ImmutableArray<TRevision> ParseRevisions(TDraftRow draft, IEnumerable<TRevisionRow> rows)
    {
        var revisionRows = rows.ToArray();
        if (revisionRows.Length > ReviewPolicy.MaximumRevisionsPerRecord)
            throw new InvalidOperationException("Stored draft revision history exceeds its limit.");
        var revisions = revisionRows.Select(row => ReviewJson.Deserialize<TRevision>(row.RevisionJson, "revision")).ToImmutableArray();
        if (revisions.Length != draft.CurrentRevisionNumber ||
            revisions.Select((revision, index) => revision.RevisionNumber == index + 1 && revision.RecordId == draft.RecordId)
                .Any(valid => !valid))
            throw new InvalidOperationException("Stored draft revision history is inconsistent.");
        foreach (var revision in revisions) revision.ValidateShape();
        return revisions;
    }

    private sealed class UnpublishedKey
    {
        public string RecordId { get; set; } = "";
        public int CurrentRevisionNumber { get; set; }
        public long CreatedAtUtcTicks { get; set; }
        public int? PublishedRevisionNumber { get; set; }
    }
}
