using System.Data;
using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents.Persistence;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Review.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Infrastructure.Review;

/// <summary>Transactional persistence for immutable document-change revisions and review decisions.</summary>
public sealed class PostgresDocumentChangeReviewStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
    : IDocumentChangeReviewStore
{
    private const int MaximumPageSize = 100;
    private const int MaximumPublicationDrafts = 64;
    private const string CreateOperation = "create";
    private const string SaveOperation = "save";
    private const string DecideOperation = "decide";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static PostgresDocumentChangeReviewStore FromConnectionString(string connectionString)
    {
        _ = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
    }

    public async Task<DocumentChangeDraftRevision> CreateAsync(string actorSubject,
        DocumentChangeDraftRevision revision, string idempotencyKey, string payloadHash,
        CancellationToken cancellationToken)
    {
        ValidateActorAndIdempotency(actorSubject, idempotencyKey, payloadHash);
        ArgumentNullException.ThrowIfNull(revision);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await BeginWriteAsync(db, cancellationToken);
        var replay = await ReadReplayAsync<DocumentChangeDraftRevision>(db, actorSubject, CreateOperation,
            idempotencyKey, payloadHash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        if (revision.RevisionNumber != 1) throw new InvalidOperationException("A draft must begin at revision one.");
        if (revision.AuthorSubject != actorSubject)
            throw new UnauthorizedAccessException("Revision author does not match the authenticated subject.");
        var eligible = await ReadEligibleComparisonAsync(db, revision.ComparisonId, cancellationToken)
            ?? throw new ArgumentException("Drafts must reference a complete comparison containing a text change.", nameof(revision));
        revision = revision with { Citations = CreateInitialCitations(eligible.Comparison) };
        await ValidateRevisionAgainstEvidenceAsync(db, revision, cancellationToken);
        var revisionJson = Serialize(revision);
        db.Add(new DocumentChangeDraftRow
        {
            DraftId = revision.DraftId,
            ComparisonId = revision.ComparisonId,
            CurrentRevisionNumber = 1,
            ReviewStateVersion = 0,
            CreatedAtUtcTicks = DateTimeOffset.UtcNow.UtcTicks
        });
        db.Add(new DocumentChangeRevisionRow
        {
            DraftId = revision.DraftId,
            RevisionNumber = 1,
            RevisionJson = revisionJson,
            CreatedAtUtcTicks = revision.CreatedAtUtc.UtcTicks
        });
        AddIdempotency(db, actorSubject, CreateOperation, idempotencyKey, payloadHash, revisionJson);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return revision;
    }

    public async Task<DocumentChangeDraftRevision> SaveRevisionAsync(string actorSubject,
        DocumentChangeDraftRevision revision, int expectedRevisionNumber, string idempotencyKey,
        string payloadHash, CancellationToken cancellationToken)
    {
        ValidateActorAndIdempotency(actorSubject, idempotencyKey, payloadHash);
        ArgumentNullException.ThrowIfNull(revision);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await BeginWriteAsync(db, cancellationToken);
        var replay = await ReadReplayAsync<DocumentChangeDraftRevision>(db, actorSubject, SaveOperation,
            idempotencyKey, payloadHash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        var draft = await db.Set<DocumentChangeDraftRow>().SingleOrDefaultAsync(row => row.DraftId == revision.DraftId, cancellationToken)
            ?? throw new KeyNotFoundException("Document-change draft was not found.");
        if (draft.CurrentRevisionNumber != expectedRevisionNumber || revision.RevisionNumber != expectedRevisionNumber + 1)
            throw new DocumentChangeReviewConflictException("The draft has a newer revision. Refresh before saving.");
        if (draft.CurrentRevisionNumber >= DocumentChangeReviewPolicy.MaximumRevisionsPerDraft)
            throw new ArgumentException("Draft revision history has reached its configured limit.");
        if (revision.AuthorSubject != actorSubject)
            throw new UnauthorizedAccessException("Revision author does not match the authenticated subject.");
        var current = await ReadRevisionAsync(db, revision.DraftId, draft.CurrentRevisionNumber, cancellationToken);
        revision = revision with { ComparisonId = current.ComparisonId };
        await ValidateRevisionAgainstEvidenceAsync(db, revision, cancellationToken);
        var revisionJson = Serialize(revision);
        db.Add(new DocumentChangeRevisionRow
        {
            DraftId = revision.DraftId,
            RevisionNumber = revision.RevisionNumber,
            RevisionJson = revisionJson,
            CreatedAtUtcTicks = revision.CreatedAtUtc.UtcTicks
        });
        draft.CurrentRevisionNumber = revision.RevisionNumber;
        AddIdempotency(db, actorSubject, SaveOperation, idempotencyKey, payloadHash, revisionJson);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return revision;
    }

    public async Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision,
        int expectedRevisionNumber, int expectedReviewStateVersion, string idempotencyKey,
        string payloadHash, ImmutableHashSet<string> allowedOfficialIds, ImmutableHashSet<string> allowedIssueIds,
        CancellationToken cancellationToken)
    {
        ValidateActorAndIdempotency(actorSubject, idempotencyKey, payloadHash);
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.ActorSubject != actorSubject)
            throw new UnauthorizedAccessException("Decision actor does not match the authenticated subject.");
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await BeginWriteAsync(db, cancellationToken);
        var replay = await ReadReplayAsync<ReviewDecision>(db, actorSubject, DecideOperation,
            idempotencyKey, payloadHash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        var draft = await db.Set<DocumentChangeDraftRow>().SingleOrDefaultAsync(row => row.DraftId == decision.DraftId, cancellationToken)
            ?? throw new KeyNotFoundException("Document-change draft was not found.");
        if (draft.CurrentRevisionNumber != expectedRevisionNumber || decision.RevisionNumber != expectedRevisionNumber)
            throw new DocumentChangeReviewConflictException("The draft has a newer revision. Refresh before deciding.");
        if (draft.ReviewStateVersion != expectedReviewStateVersion || decision.ReviewStateVersion != expectedReviewStateVersion + 1)
            throw new DocumentChangeReviewConflictException("Review state changed. Refresh before deciding.");
        if (draft.ReviewStateVersion >= DocumentChangeReviewPolicy.MaximumDecisionsPerDraft)
            throw new ArgumentException("Review decision history has reached its configured limit.");

        var review = await ReadAggregateAsync(db, draft, cancellationToken);
        DocumentChangeReviewPolicy.ValidateDecision(review, decision);
        if (decision.Kind == ReviewDecisionKind.Approve &&
            (review.CurrentRevision.OfficialIds.Any(id => !allowedOfficialIds.Contains(id)) ||
             review.CurrentRevision.IssueIds.Any(id => !allowedIssueIds.Contains(id))))
            throw new ArgumentException("The saved revision includes selections that are no longer configured.");
        if (decision.Kind == ReviewDecisionKind.Approve)
            await ValidateApprovalAsync(db, review.CurrentRevision, cancellationToken);

        var json = Serialize(decision);
        db.Add(new ReviewDecisionRow
        {
            DecisionId = decision.DecisionId,
            DraftId = decision.DraftId,
            RevisionNumber = decision.RevisionNumber,
            ReviewStateVersion = decision.ReviewStateVersion,
            DecisionJson = json,
            Kind = decision.Kind,
            CreatedAtUtcTicks = decision.CreatedAtUtc.UtcTicks
        });
        draft.ReviewStateVersion = decision.ReviewStateVersion;
        AddIdempotency(db, actorSubject, DecideOperation, idempotencyKey, payloadHash, json);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return decision;
    }

    public async Task<DocumentChangeReview?> GetAsync(string draftId, CancellationToken cancellationToken)
    {
        ValidateDraftId(draftId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var draft = await db.Set<DocumentChangeDraftRow>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.DraftId == draftId, cancellationToken);
        if (draft is null) return null;
        var result = await ReadAggregateAsync(db, draft, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<IReadOnlyDictionary<string, PublishableDocumentChange>> GetForPublicationAsync(
        IReadOnlyCollection<string> draftIds, CancellationToken cancellationToken)
    {
        ValidateDraftIds(draftIds);
        var ids = draftIds.ToArray();
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var drafts = await db.Set<DocumentChangeDraftRow>().AsNoTracking()
            .Where(row => ids.Contains(row.DraftId)).ToArrayAsync(cancellationToken);
        var result = await ReadAggregatesAsync(db, drafts, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<DocumentChangeReviewPage> ListAsync(PageCursor? cursor, int limit, DraftStatusFilter filter,
        CancellationToken cancellationToken)
    {
        ValidatePage(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var fetched = await KeysetDrafts(DraftsWhere(db, filter), cursor).Take(limit + 1).ToArrayAsync(cancellationToken);
        var slice = KeysetSlice<DocumentChangeDraftRow>.Create(fetched, limit, cursor, row => (row.CreatedAtUtcTicks, row.DraftId));
        var ids = slice.Items.Select(row => row.DraftId).ToArray();
        var revisions = await ReadRevisionRowsAsync(db, ids, cancellationToken);
        var decisions = await ReadDecisionRowsAsync(db, ids, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var items = slice.Items.Select(row => ToListItem(row, revisions[row.DraftId], decisions[row.DraftId])).ToImmutableArray();
        return new(items, slice.NewerCursor, slice.OlderCursor);
    }

    public async Task<EligibleDocumentComparisonPage> ListEligibleComparisonsAsync(PageCursor? cursor, int limit,
        CancellationToken cancellationToken)
    {
        ValidatePage(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var keys = from comparison in db.Set<DocumentComparisonRow>().FromSqlRaw(ReviewQueries.EligibleComparisons)
                   join extraction in db.Set<DocumentExtractionRow>() on comparison.AfterExtractionId equals extraction.ExtractionId
                   join attempt in db.Attempts on extraction.AttemptId equals attempt.AttemptId
                   select new EligibleKey { ComparisonId = comparison.ComparisonId, ObservedTicks = attempt.ObservedAtUtcTicks };
        var fetched = await KeysetComparisons(keys.AsNoTracking(), cursor).Take(limit + 1).ToArrayAsync(cancellationToken);
        var slice = KeysetSlice<EligibleKey>.Create(fetched, limit, cursor, key => (key.ObservedTicks, key.ComparisonId));
        var verified = await ReadVerifiedComparisonsAsync(db, slice.Items.Select(key => key.ComparisonId).ToArray(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var items = slice.Items.Select(key => verified.GetValueOrDefault(key.ComparisonId)?.Summary)
            .OfType<EligibleDocumentComparison>().ToImmutableArray();
        return new(items, slice.NewerCursor, slice.OlderCursor);
    }

    public async Task<ReviewOverview> GetOverviewAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var counts = await db.Database.SqlQueryRaw<OverviewCounts>(ReviewQueries.Overview).SingleAsync(cancellationToken);
        return new(counts.NewChanges, counts.DraftsNeedingAction, counts.ApprovedDrafts);
    }

    public async Task<IReadOnlyList<ReviewActivityEvent>> ListRecentActivityAsync(int limit, CancellationToken cancellationToken)
    {
        ValidatePage(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var drafts = await db.Set<DocumentChangeDraftRow>().AsNoTracking().OrderByDescending(row => row.CreatedAtUtcTicks)
            .ThenByDescending(row => row.DraftId).Take(limit).ToArrayAsync(cancellationToken);
        var saved = await db.Set<DocumentChangeRevisionRow>().AsNoTracking().Where(row => row.RevisionNumber > 1)
            .OrderByDescending(row => row.CreatedAtUtcTicks).ThenByDescending(row => row.DraftId)
            .ThenByDescending(row => row.RevisionNumber).Take(limit).ToArrayAsync(cancellationToken);
        var decided = await db.Set<ReviewDecisionRow>().AsNoTracking().OrderByDescending(row => row.CreatedAtUtcTicks)
            .ThenByDescending(row => row.DecisionId).Take(limit).ToArrayAsync(cancellationToken);
        var revisions = await ReadActivityRevisionsAsync(db, drafts, decided, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return
        [
            .. drafts.Select(row => ToActivity(ReviewActivityKind.DraftCreated, row.CreatedAtUtcTicks, revisions[(row.DraftId, 1)])),
            .. saved.Select(row => ToActivity(ReviewActivityKind.RevisionSaved, row.CreatedAtUtcTicks,
                Deserialize<DocumentChangeDraftRevision>(row.RevisionJson, "revision"))),
            .. decided.Select(row => ToDecisionActivity(row, revisions[(row.DraftId, row.RevisionNumber)]))
        ];
    }

    private static async Task<Dictionary<(string DraftId, int RevisionNumber), DocumentChangeDraftRevision>> ReadActivityRevisionsAsync(
        CollectionAttemptDbContext db, DocumentChangeDraftRow[] drafts, ReviewDecisionRow[] decisions,
        CancellationToken cancellationToken)
    {
        var wanted = drafts.Select(row => (row.DraftId, RevisionNumber: 1))
            .Concat(decisions.Select(row => (row.DraftId, row.RevisionNumber))).ToHashSet();
        var draftIds = wanted.Select(key => key.DraftId).Distinct().ToArray();
        var numbers = wanted.Select(key => key.RevisionNumber).Distinct().ToArray();
        var rows = await db.Set<DocumentChangeRevisionRow>().AsNoTracking()
            .Where(row => draftIds.Contains(row.DraftId) && numbers.Contains(row.RevisionNumber))
            .ToArrayAsync(cancellationToken);
        return rows.Where(row => wanted.Contains((row.DraftId, row.RevisionNumber))).ToDictionary(
            row => (row.DraftId, row.RevisionNumber), row => Deserialize<DocumentChangeDraftRevision>(row.RevisionJson, "revision"));
    }

    private static ReviewActivityEvent ToActivity(ReviewActivityKind kind, long ticks, DocumentChangeDraftRevision revision) =>
        new(kind, new DateTimeOffset(ticks, TimeSpan.Zero), revision.AuthorSubject, revision.DraftId, revision.Headline, null);

    private static ReviewActivityEvent ToDecisionActivity(ReviewDecisionRow row, DocumentChangeDraftRevision revision) =>
        new(ReviewActivityKind.DecisionRecorded, new DateTimeOffset(row.CreatedAtUtcTicks, TimeSpan.Zero),
            Deserialize<ReviewDecision>(row.DecisionJson, "decision").ActorSubject, row.DraftId, revision.Headline, row.Kind);

    private static IQueryable<DocumentChangeDraftRow> DraftsWhere(CollectionAttemptDbContext db, DraftStatusFilter filter) =>
        (filter switch
        {
            DraftStatusFilter.All => db.Set<DocumentChangeDraftRow>(),
            DraftStatusFilter.NeedsAction => db.Set<DocumentChangeDraftRow>().FromSqlRaw(ReviewQueries.NeedsActionDrafts),
            DraftStatusFilter.Approved => db.Set<DocumentChangeDraftRow>().FromSqlRaw(ReviewQueries.ApprovedDrafts),
            _ => throw new ArgumentOutOfRangeException(nameof(filter))
        }).AsNoTracking();

    private static IQueryable<DocumentChangeDraftRow> KeysetDrafts(IQueryable<DocumentChangeDraftRow> rows, PageCursor? cursor)
    {
        if (cursor is null) return rows.OrderByDescending(row => row.CreatedAtUtcTicks).ThenByDescending(row => row.DraftId);
        var (ticks, id) = (cursor.Ticks, cursor.Id);
        return cursor.Direction == PageDirection.Older
            ? rows.Where(row => row.CreatedAtUtcTicks < ticks || row.CreatedAtUtcTicks == ticks && string.Compare(row.DraftId, id) < 0)
                .OrderByDescending(row => row.CreatedAtUtcTicks).ThenByDescending(row => row.DraftId)
            : rows.Where(row => row.CreatedAtUtcTicks > ticks || row.CreatedAtUtcTicks == ticks && string.Compare(row.DraftId, id) > 0)
                .OrderBy(row => row.CreatedAtUtcTicks).ThenBy(row => row.DraftId);
    }

    private static IQueryable<EligibleKey> KeysetComparisons(IQueryable<EligibleKey> keys, PageCursor? cursor)
    {
        if (cursor is null) return keys.OrderByDescending(key => key.ObservedTicks).ThenByDescending(key => key.ComparisonId);
        var (ticks, id) = (cursor.Ticks, cursor.Id);
        return cursor.Direction == PageDirection.Older
            ? keys.Where(key => key.ObservedTicks < ticks || key.ObservedTicks == ticks && string.Compare(key.ComparisonId, id) < 0)
                .OrderByDescending(key => key.ObservedTicks).ThenByDescending(key => key.ComparisonId)
            : keys.Where(key => key.ObservedTicks > ticks || key.ObservedTicks == ticks && string.Compare(key.ComparisonId, id) > 0)
                .OrderBy(key => key.ObservedTicks).ThenBy(key => key.ComparisonId);
    }

    private sealed record EligibleKey
    {
        public string ComparisonId { get; init; } = "";
        public long ObservedTicks { get; init; }
    }

    private sealed class OverviewCounts
    {
        public int NewChanges { get; set; }
        public int DraftsNeedingAction { get; set; }
        public int ApprovedDrafts { get; set; }
    }

    private static async Task<EligibleDocumentComparison?> ReadEligibleComparisonAsync(CollectionAttemptDbContext db,
        string comparisonId, CancellationToken cancellationToken) =>
        (await ReadVerifiedComparisonAsync(db, comparisonId, cancellationToken))?.Summary;

    private sealed record VerifiedComparison(EligibleDocumentComparison Summary,
        DocumentExtraction Before, DocumentExtraction After);

    private static async Task<VerifiedComparison?> ReadVerifiedComparisonAsync(CollectionAttemptDbContext db,
        string comparisonId, CancellationToken cancellationToken) =>
        (await ReadVerifiedComparisonsAsync(db, [comparisonId], cancellationToken)).GetValueOrDefault(comparisonId);

    private static async Task<Dictionary<string, VerifiedComparison>> ReadVerifiedComparisonsAsync(
        CollectionAttemptDbContext db, string[] comparisonIds, CancellationToken cancellationToken)
    {
        var rows = await db.Set<DocumentComparisonRow>().AsNoTracking()
            .Where(row => comparisonIds.Contains(row.ComparisonId)).ToArrayAsync(cancellationToken);
        var extractionIds = rows.SelectMany(row => new[] { row.BeforeExtractionId, row.AfterExtractionId })
            .Distinct(StringComparer.Ordinal).ToArray();
        var extractions = await PostgresDocumentExtractionStore.ReadManyAsync(db, extractionIds, cancellationToken);
        var verified = new Dictionary<string, VerifiedComparison>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var comparison = VerifyComparison(row, extractions, cancellationToken);
            if (comparison is not null) verified.Add(row.ComparisonId, comparison);
        }
        return verified;
    }

    private static VerifiedComparison? VerifyComparison(DocumentComparisonRow row,
        Dictionary<string, DocumentExtraction> extractions, CancellationToken cancellationToken)
    {
        var before = extractions.GetValueOrDefault(row.BeforeExtractionId)
            ?? throw new InvalidOperationException("Comparison's before extraction is missing.");
        var after = extractions.GetValueOrDefault(row.AfterExtractionId)
            ?? throw new InvalidOperationException("Comparison's after extraction is missing.");
        var comparison = DocumentComparison.Create(before, after, cancellationToken);
        if (row.ComparisonId != comparison.ComparisonId || row.AlgorithmVersion != comparison.AlgorithmVersion ||
            row.SettingsVersion != comparison.SettingsVersion || row.ResultJson != JsonSerializer.Serialize(comparison, CollectionProtocol.JsonOptions))
            throw new InvalidOperationException("Stored comparison does not match its retained extraction evidence.");
        if (comparison.Status != DocumentComparisonStatus.Complete || comparison.Hunks.IsEmpty) return null;
        if (before.SourceAttempt.SourceId != after.SourceAttempt.SourceId ||
            before.SourceAttempt.RequestedUrl != after.SourceAttempt.RequestedUrl)
            throw new InvalidOperationException("Stored comparison sources are incompatible.");
        return new(new(before.SourceAttempt.SourceId, before.SourceAttempt.RequestedUrl,
            before.SourceAttempt.FinalUrl, after.SourceAttempt.FinalUrl,
            before.SourceAttempt.ObservedAt, after.SourceAttempt.ObservedAt, comparison), before, after);
    }

    private static ImmutableArray<DocumentChangeCitation> CreateInitialCitations(DocumentComparison comparison)
    {
        var citations = ImmutableArray.CreateBuilder<DocumentChangeCitation>();
        foreach (var hunk in comparison.Hunks.Take(32))
        {
            if (hunk.BeforeLength > 0)
                citations.Add(new(comparison.BeforeExtractionId, hunk.BeforeStart, hunk.BeforeLength));
            if (hunk.AfterLength > 0)
                citations.Add(new(comparison.AfterExtractionId, hunk.AfterStart, hunk.AfterLength));
        }
        return citations.ToImmutable();
    }

    private static async Task ValidateRevisionAgainstEvidenceAsync(CollectionAttemptDbContext db,
        DocumentChangeDraftRevision revision, CancellationToken cancellationToken)
    {
        var verified = await ReadVerifiedComparisonAsync(db, revision.ComparisonId, cancellationToken)
            ?? throw new ArgumentException("Drafts must reference a complete comparison containing a text change.", nameof(revision));
        ValidateRevisionAgainstEvidence(revision, verified);
    }

    private static void ValidateRevisionAgainstEvidence(DocumentChangeDraftRevision revision, VerifiedComparison verified)
    {
        ValidateRevisionShape(revision);
        if (revision.ComparisonId != verified.Summary.Comparison.ComparisonId)
            throw new InvalidOperationException("Revision comparison binding is inconsistent.");
        foreach (var citation in revision.Citations)
        {
            var extraction = citation.ExtractionId == verified.Before.ExtractionId ? verified.Before
                : citation.ExtractionId == verified.After.ExtractionId ? verified.After
                : throw new ArgumentException("Citations must bind to this comparison's before or after extraction.", nameof(revision));
            _ = new DocumentTextSpan(extraction, citation.Start, citation.Length);
        }
    }

    private static async Task ValidateApprovalAsync(CollectionAttemptDbContext db,
        DocumentChangeDraftRevision revision, CancellationToken cancellationToken)
    {
        revision.ValidateForApproval();
        await ValidateRevisionAgainstEvidenceAsync(db, revision, cancellationToken);
    }

    private static void ValidateRevisionShape(DocumentChangeDraftRevision revision)
    {
        if (revision.DraftId is not { Length: 32 } || revision.DraftId.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) ||
            revision.RevisionNumber < 1 || string.IsNullOrWhiteSpace(revision.AuthorSubject) || revision.AuthorSubject.Length > 256 ||
            revision.CreatedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Draft revision identity is invalid.", nameof(revision));
        ValidateHash(revision.ComparisonId, nameof(revision));
        ValidateText(revision.Headline, 256, nameof(revision.Headline));
        ValidateText(revision.Summary, 8_000, nameof(revision.Summary));
        ValidateText(revision.Significance, 4_000, nameof(revision.Significance));
        ValidateText(revision.Limits, 4_000, nameof(revision.Limits));
        ValidateText(revision.Institution, 256, nameof(revision.Institution));
        if ((revision.ChangeDate is null) != (revision.ChangeDateEvidence is null) ||
            revision.ChangeDateEvidence is not null && !revision.Citations.Contains(revision.ChangeDateEvidence))
            throw new ArgumentException("Change date must be bound to a citation included in the public evidence bindings.", nameof(revision));
        ValidateIds(revision.OfficialIds, nameof(revision.OfficialIds));
        ValidateIds(revision.IssueIds, nameof(revision.IssueIds));
        if (revision.Citations.IsDefault || revision.Citations.Length > 64)
            throw new ArgumentException("Revision must contain at most 64 citations.", nameof(revision));
        if (revision.Citations.Any(citation => citation is null || citation.Start < 0 || citation.Length is <= 0 or > 8192) ||
            revision.Citations.Distinct().Count() != revision.Citations.Length)
            throw new ArgumentException("Citations must be distinct valid bounded spans.", nameof(revision));
    }

    private static async Task<DocumentChangeReview> ReadAggregateAsync(CollectionAttemptDbContext db,
        DocumentChangeDraftRow draft, CancellationToken cancellationToken) =>
        (await ReadAggregatesAsync(db, [draft], cancellationToken))[draft.DraftId].Review;

    private static async Task<Dictionary<string, PublishableDocumentChange>> ReadAggregatesAsync(
        CollectionAttemptDbContext db, DocumentChangeDraftRow[] drafts, CancellationToken cancellationToken)
    {
        if (drafts.Length == 0) return [];
        var draftIds = drafts.Select(draft => draft.DraftId).ToArray();
        var revisions = await ReadRevisionRowsAsync(db, draftIds, cancellationToken);
        var decisions = await ReadDecisionRowsAsync(db, draftIds, cancellationToken);
        var comparisonIds = drafts.Select(draft => draft.ComparisonId).Distinct(StringComparer.Ordinal).ToArray();
        var comparisons = await ReadVerifiedComparisonsAsync(db, comparisonIds, cancellationToken);
        return drafts.ToDictionary(draft => draft.DraftId,
            draft => BuildAggregate(draft, revisions[draft.DraftId], decisions[draft.DraftId], comparisons),
            StringComparer.Ordinal);
    }

    private static PublishableDocumentChange BuildAggregate(DocumentChangeDraftRow draft,
        IEnumerable<DocumentChangeRevisionRow> revisionRows, IEnumerable<ReviewDecisionRow> decisionRows,
        Dictionary<string, VerifiedComparison> comparisons)
    {
        var revisions = ParseRevisions(draft, revisionRows);
        var verified = comparisons.GetValueOrDefault(draft.ComparisonId)
            ?? throw new InvalidOperationException("Retained draft comparison is no longer valid.");
        foreach (var revision in revisions) ValidateRevisionAgainstEvidence(revision, verified);
        var decisions = ParseDecisions(draft, decisionRows);
        ValidateDecisionHistory(draft.DraftId, revisions, decisions);
        var concerns = DocumentChangeReviewPolicy.GetUnresolvedConcerns(decisions);
        var review = new DocumentChangeReview(draft.DraftId, revisions[^1], draft.ReviewStateVersion, revisions,
            decisions, concerns);
        return new(review, verified.Summary, verified.Before, verified.After);
    }

    private static async Task<ILookup<string, DocumentChangeRevisionRow>> ReadRevisionRowsAsync(
        CollectionAttemptDbContext db, string[] draftIds, CancellationToken cancellationToken)
    {
        var rows = await db.Set<DocumentChangeRevisionRow>().AsNoTracking()
            .Where(row => draftIds.Contains(row.DraftId)).OrderBy(row => row.DraftId).ThenBy(row => row.RevisionNumber)
            .Take(draftIds.Length * (DocumentChangeReviewPolicy.MaximumRevisionsPerDraft + 1))
            .ToArrayAsync(cancellationToken);
        return rows.ToLookup(row => row.DraftId, StringComparer.Ordinal);
    }

    private static async Task<ILookup<string, ReviewDecisionRow>> ReadDecisionRowsAsync(
        CollectionAttemptDbContext db, string[] draftIds, CancellationToken cancellationToken)
    {
        var rows = await db.Set<ReviewDecisionRow>().AsNoTracking()
            .Where(row => draftIds.Contains(row.DraftId)).OrderBy(row => row.DraftId).ThenBy(row => row.ReviewStateVersion)
            .Take(draftIds.Length * (DocumentChangeReviewPolicy.MaximumDecisionsPerDraft + 1))
            .ToArrayAsync(cancellationToken);
        return rows.ToLookup(row => row.DraftId, StringComparer.Ordinal);
    }

    private static ImmutableArray<DocumentChangeDraftRevision> ParseRevisions(DocumentChangeDraftRow draft,
        IEnumerable<DocumentChangeRevisionRow> rows)
    {
        var revisionRows = rows.ToArray();
        if (revisionRows.Length > DocumentChangeReviewPolicy.MaximumRevisionsPerDraft)
            throw new InvalidOperationException("Stored draft revision history exceeds its limit.");
        var revisions = revisionRows.Select(row => Deserialize<DocumentChangeDraftRevision>(row.RevisionJson, "revision"))
            .ToImmutableArray();
        if (revisions.IsEmpty || revisions.Length != draft.CurrentRevisionNumber ||
            revisions.Select((revision, index) => revision.RevisionNumber == index + 1 && revision.DraftId == draft.DraftId).Any(valid => !valid))
            throw new InvalidOperationException("Stored draft revision history is inconsistent.");
        if (revisions.Any(revision => revision.ComparisonId != draft.ComparisonId))
            throw new InvalidOperationException("A saved revision changed its bound comparison.");
        foreach (var revision in revisions) ValidateRevisionShape(revision);
        return revisions;
    }

    private static ImmutableArray<ReviewDecision> ParseDecisions(DocumentChangeDraftRow draft,
        IEnumerable<ReviewDecisionRow> rows)
    {
        var decisionRows = rows.ToArray();
        if (decisionRows.Length > DocumentChangeReviewPolicy.MaximumDecisionsPerDraft)
            throw new InvalidOperationException("Stored review decision history exceeds its limit.");
        var decisions = decisionRows.Select(row =>
        {
            var decision = Deserialize<ReviewDecision>(row.DecisionJson, "decision");
            if (decision.DecisionId != row.DecisionId || decision.DraftId != row.DraftId ||
                decision.RevisionNumber != row.RevisionNumber || decision.ReviewStateVersion != row.ReviewStateVersion)
                throw new InvalidOperationException("Stored decision identity is inconsistent.");
            return decision;
        }).ToImmutableArray();
        if (decisions.Length != draft.ReviewStateVersion ||
            decisions.Select((decision, index) => decision.ReviewStateVersion == index + 1).Any(valid => !valid))
            throw new InvalidOperationException("Stored review decision history is inconsistent.");
        return decisions;
    }

    private static void ValidateDecisionHistory(string draftId,
        ImmutableArray<DocumentChangeDraftRevision> revisions, ImmutableArray<ReviewDecision> decisions)
    {
        var priorDecisions = ImmutableArray<ReviewDecision>.Empty;
        foreach (var decision in decisions)
        {
            if (decision.RevisionNumber < 1 || decision.RevisionNumber > revisions.Length)
                throw new InvalidOperationException("Stored decision references a missing revision.");
            var targetRevision = revisions[decision.RevisionNumber - 1];
            var priorConcerns = DocumentChangeReviewPolicy.GetUnresolvedConcerns(priorDecisions);
            var historicalReview = new DocumentChangeReview(draftId, targetRevision,
                priorDecisions.Length, revisions.Take(decision.RevisionNumber).ToImmutableArray(),
                priorDecisions, priorConcerns);
            DocumentChangeReviewPolicy.ValidateDecision(historicalReview, decision);
            if (decision.Kind == ReviewDecisionKind.Approve) targetRevision.ValidateForApproval();
            priorDecisions = priorDecisions.Add(decision);
        }
    }

    private static DocumentChangeReviewListItem ToListItem(DocumentChangeDraftRow draft,
        IEnumerable<DocumentChangeRevisionRow> revisionRows, IEnumerable<ReviewDecisionRow> decisionRows)
    {
        var revisions = ParseRevisions(draft, revisionRows);
        var decisions = ParseDecisions(draft, decisionRows);
        ValidateDecisionHistory(draft.DraftId, revisions, decisions);
        var currentRevision = revisions[^1];
        var concerns = DocumentChangeReviewPolicy.GetUnresolvedConcerns(decisions);
        var status = DocumentChangeReviewPolicy.GetCurrentStatus(currentRevision, decisions, concerns);
        return new(draft.DraftId, currentRevision, draft.ReviewStateVersion, status, concerns.Length);
    }

    private static async Task<DocumentChangeDraftRevision> ReadRevisionAsync(CollectionAttemptDbContext db,
        string draftId, int revisionNumber, CancellationToken cancellationToken)
    {
        var row = await db.Set<DocumentChangeRevisionRow>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.DraftId == draftId && candidate.RevisionNumber == revisionNumber, cancellationToken)
            ?? throw new InvalidOperationException("Current draft revision is missing.");
        var revision = Deserialize<DocumentChangeDraftRevision>(row.RevisionJson, "revision");
        if (revision.DraftId != draftId || revision.RevisionNumber != revisionNumber)
            throw new InvalidOperationException("Stored revision identity is inconsistent.");
        return revision;
    }

    private static async Task<T?> ReadReplayAsync<T>(CollectionAttemptDbContext db, string actorSubject,
        string operation, string idempotencyKey, string payloadHash, CancellationToken cancellationToken) where T : class
    {
        var row = await db.Set<ReviewIdempotencyRow>().AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.ActorSubject == actorSubject && candidate.Operation == operation &&
            candidate.IdempotencyKey == idempotencyKey, cancellationToken);
        if (row is null) return null;
        if (row.PayloadHash != payloadHash)
            throw new ArgumentException("Idempotency key was already used with a different request payload.");
        return Deserialize<T>(row.ResultJson, "idempotency result");
    }

    private static void AddIdempotency(CollectionAttemptDbContext db, string actorSubject, string operation,
        string idempotencyKey, string payloadHash, string resultJson) => db.Add(new ReviewIdempotencyRow
        {
            ActorSubject = actorSubject,
            Operation = operation,
            IdempotencyKey = idempotencyKey,
            PayloadHash = payloadHash,
            ResultJson = resultJson
        });

    internal static async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginWriteAsync(
        CollectionAttemptDbContext db, CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended('civic-lens-document-change-review', 0))", cancellationToken);
        return transaction;
    }

    private static T Deserialize<T>(string json, string kind) => JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new InvalidOperationException($"Stored {kind} is invalid.");

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static void ValidateActorAndIdempotency(string actorSubject, string key, string hash)
    {
        if (string.IsNullOrWhiteSpace(actorSubject) || actorSubject.Length > 256)
            throw new ArgumentException("Actor subject is invalid.", nameof(actorSubject));
        if (string.IsNullOrWhiteSpace(key) || key.Length > 256)
            throw new ArgumentException("Idempotency key is invalid.", nameof(key));
        ValidateHash(hash, nameof(hash));
    }

    private static void ValidateDraftId(string value)
    {
        if (value is not { Length: 32 } || value.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("Draft ID is invalid.", nameof(value));
    }

    private static void ValidateDraftIds(IReadOnlyCollection<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count is < 1 or > MaximumPublicationDrafts) throw new ArgumentOutOfRangeException(nameof(values));
        foreach (var value in values) ValidateDraftId(value);
    }

    private static void ValidateHash(string value, string parameterName)
    {
        if (value is not { Length: 64 } || value.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("Identifier must be a lowercase SHA-256 value.", parameterName);
    }

    private static void ValidatePage(int limit)
    {
        if (limit is < 1 or > MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(limit));
    }

    private static void ValidateText(string? value, int max, string name)
    {
        if (value is not null && (value.Length > max || value.Contains('\0')))
            throw new ArgumentException("Stored editorial text exceeds its bounds or contains NUL.", name);
    }

    private static void ValidateIds(ImmutableArray<string> values, string name)
    {
        if (values.IsDefault || values.Length > 64 || values.Any(string.IsNullOrWhiteSpace) ||
            values.Any(value => value.Length > 128) || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new ArgumentException("Stored editorial selections are invalid.", name);
    }
}
