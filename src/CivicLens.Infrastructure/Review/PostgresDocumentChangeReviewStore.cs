using System.Data;
using System.Collections.Immutable;
using System.Text.Json;
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
    private const int ComparisonScanSize = 500;
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
        db.Add(new DocumentChangeRevisionRow { DraftId = revision.DraftId, RevisionNumber = 1, RevisionJson = revisionJson });
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
            RevisionJson = revisionJson
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
            DecisionJson = json
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

    public async Task<DocumentChangeReviewPage> ListAsync(string? cursor, int limit, CancellationToken cancellationToken)
    {
        ValidatePage(cursor, limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var rows = await db.Set<DocumentChangeDraftRow>().AsNoTracking()
            .Where(row => cursor == null || string.Compare(row.DraftId, cursor) > 0)
            .OrderBy(row => row.DraftId).Take(limit + 1).ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > limit;
        var items = ImmutableArray.CreateBuilder<DocumentChangeReviewListItem>(Math.Min(limit, rows.Length));
        foreach (var row in rows.Take(limit)) items.Add(await ReadListItemAsync(db, row, cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return new(items.ToImmutable(), hasMore ? rows[limit - 1].DraftId : null);
    }

    public async Task<EligibleDocumentComparison?> GetEligibleComparisonAsync(string comparisonId,
        CancellationToken cancellationToken)
    {
        ValidateHash(comparisonId, nameof(comparisonId));
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var result = await ReadEligibleComparisonAsync(db, comparisonId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<EligibleDocumentComparisonPage> ListEligibleComparisonsAsync(string? cursor, int limit,
        CancellationToken cancellationToken)
    {
        ValidatePage(cursor, limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var items = ImmutableArray.CreateBuilder<EligibleDocumentComparison>(limit);
        var scanSize = Math.Min(ComparisonScanSize, limit * 4);
        var rows = await db.Set<DocumentComparisonRow>().AsNoTracking()
            .Where(row => cursor == null || string.Compare(row.ComparisonId, cursor) > 0)
            .OrderBy(row => row.ComparisonId).Take(scanSize + 1).ToArrayAsync(cancellationToken);
        string? nextCursor = null;
        var consumed = 0;
        foreach (var row in rows.Take(scanSize))
        {
            var item = await ReadEligibleComparisonAsync(db, row.ComparisonId, cancellationToken);
            if (item is not null) items.Add(item);
            consumed++;
            if (items.Count == limit) break;
        }
        if (consumed < rows.Length) nextCursor = rows[consumed - 1].ComparisonId;
        await transaction.CommitAsync(cancellationToken);
        return new(items.ToImmutable(), nextCursor);
    }

    private static async Task<EligibleDocumentComparison?> ReadEligibleComparisonAsync(CollectionAttemptDbContext db,
        string comparisonId, CancellationToken cancellationToken) =>
        (await ReadVerifiedComparisonAsync(db, comparisonId, cancellationToken))?.Summary;

    private sealed record VerifiedComparison(EligibleDocumentComparison Summary,
        DocumentExtraction Before, DocumentExtraction After);

    private static async Task<VerifiedComparison?> ReadVerifiedComparisonAsync(CollectionAttemptDbContext db,
        string comparisonId, CancellationToken cancellationToken)
    {
        var row = await db.Set<DocumentComparisonRow>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.ComparisonId == comparisonId, cancellationToken);
        if (row is null) return null;
        var before = await PostgresDocumentExtractionStore.ReadAsync(db, row.BeforeExtractionId, cancellationToken)
            ?? throw new InvalidOperationException("Comparison's before extraction is missing.");
        var after = await PostgresDocumentExtractionStore.ReadAsync(db, row.AfterExtractionId, cancellationToken)
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
        DocumentChangeDraftRow draft, CancellationToken cancellationToken)
    {
        var revisionRows = await db.Set<DocumentChangeRevisionRow>().AsNoTracking()
            .Where(row => row.DraftId == draft.DraftId).OrderBy(row => row.RevisionNumber)
            .Take(DocumentChangeReviewPolicy.MaximumRevisionsPerDraft + 1).ToArrayAsync(cancellationToken);
        if (revisionRows.Length > DocumentChangeReviewPolicy.MaximumRevisionsPerDraft)
            throw new InvalidOperationException("Stored draft revision history exceeds its limit.");
        var revisions = revisionRows.Select(row => Deserialize<DocumentChangeDraftRevision>(row.RevisionJson, "revision"))
            .ToImmutableArray();
        if (revisions.IsEmpty || revisions.Length != draft.CurrentRevisionNumber ||
            revisions.Select((revision, index) => revision.RevisionNumber == index + 1 && revision.DraftId == draft.DraftId).Any(valid => !valid))
            throw new InvalidOperationException("Stored draft revision history is inconsistent.");
        if (revisions.Any(revision => revision.ComparisonId != draft.ComparisonId))
            throw new InvalidOperationException("A saved revision changed its bound comparison.");
        var verified = await ReadVerifiedComparisonAsync(db, draft.ComparisonId, cancellationToken)
            ?? throw new InvalidOperationException("Retained draft comparison is no longer valid.");
        foreach (var revision in revisions) ValidateRevisionAgainstEvidence(revision, verified);
        var decisionRows = await db.Set<ReviewDecisionRow>().AsNoTracking()
            .Where(row => row.DraftId == draft.DraftId).OrderBy(row => row.ReviewStateVersion)
            .Take(DocumentChangeReviewPolicy.MaximumDecisionsPerDraft + 1).ToArrayAsync(cancellationToken);
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
        var priorDecisions = ImmutableArray<ReviewDecision>.Empty;
        foreach (var decision in decisions)
        {
            if (decision.RevisionNumber > revisions.Length)
                throw new InvalidOperationException("Stored decision references a missing revision.");
            var targetRevision = revisions[decision.RevisionNumber - 1];
            var priorConcerns = DocumentChangeReviewPolicy.GetUnresolvedConcerns(priorDecisions);
            var historicalReview = new DocumentChangeReview(draft.DraftId, targetRevision,
                priorDecisions.Length, revisions.Take(decision.RevisionNumber).ToImmutableArray(),
                priorDecisions, priorConcerns);
            DocumentChangeReviewPolicy.ValidateDecision(historicalReview, decision);
            if (decision.Kind == ReviewDecisionKind.Approve) targetRevision.ValidateForApproval();
            priorDecisions = priorDecisions.Add(decision);
        }
        var concerns = DocumentChangeReviewPolicy.GetUnresolvedConcerns(decisions);
        return new(draft.DraftId, revisions[^1], draft.ReviewStateVersion, revisions, decisions, concerns);
    }

    private static async Task<DocumentChangeReviewListItem> ReadListItemAsync(CollectionAttemptDbContext db,
        DocumentChangeDraftRow draft, CancellationToken cancellationToken)
    {
        var currentRevision = await ReadRevisionAsync(db, draft.DraftId, draft.CurrentRevisionNumber, cancellationToken);
        ValidateRevisionShape(currentRevision);
        if (currentRevision.ComparisonId != draft.ComparisonId)
            throw new InvalidOperationException("Current revision has a different comparison binding.");
        var rows = await db.Set<ReviewDecisionRow>().AsNoTracking()
            .Where(row => row.DraftId == draft.DraftId).OrderBy(row => row.ReviewStateVersion)
            .Take(DocumentChangeReviewPolicy.MaximumDecisionsPerDraft + 1).ToArrayAsync(cancellationToken);
        if (rows.Length > DocumentChangeReviewPolicy.MaximumDecisionsPerDraft || rows.Length != draft.ReviewStateVersion)
            throw new InvalidOperationException("Stored review decision history is inconsistent or exceeds its limit.");
        var decisions = rows.Select(row =>
        {
            var decision = Deserialize<ReviewDecision>(row.DecisionJson, "decision");
            if (decision.DecisionId != row.DecisionId || decision.DraftId != draft.DraftId ||
                decision.RevisionNumber != row.RevisionNumber || decision.ReviewStateVersion != row.ReviewStateVersion)
                throw new InvalidOperationException("Stored decision identity is inconsistent.");
            return decision;
        }).ToImmutableArray();
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

    private static async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginWriteAsync(
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

    private static void ValidateHash(string value, string parameterName)
    {
        if (value is not { Length: 64 } || value.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("Identifier must be a lowercase SHA-256 value.", parameterName);
    }

    private static void ValidatePage(string? cursor, int limit)
    {
        if (limit is < 1 or > MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(limit));
        if (cursor is not null && (cursor.Length == 0 || cursor.Length > 256)) throw new ArgumentException("Cursor is invalid.", nameof(cursor));
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
