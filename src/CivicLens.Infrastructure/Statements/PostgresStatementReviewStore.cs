using System.Collections.Immutable;
using System.Data;
using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Application.Statements;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Statements.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Infrastructure.Statements;

public sealed class PostgresStatementReviewStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IStatementReviewStore
{
    private readonly AdmittingRevisionReader admittingRevisions = new();

    private const string CreateOperation = "statement-create";
    private const string SaveOperation = "statement-save";
    private const string DecideOperation = "statement-decide";

    public static PostgresStatementReviewStore FromConnectionString(string connectionString)
    {
        _ = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
    }

    public async Task<StatementDraftRevision> CreateAsync(string actorSubject, StatementDraftRevision revision, string idempotencyKey,
        string payloadHash, CancellationToken cancellationToken)
    {
        ReviewIdempotency.ValidateRequest(actorSubject, idempotencyKey, payloadHash);
        ArgumentNullException.ThrowIfNull(revision);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await ReviewIdempotency.WriteOnceAsync(db, ReviewWriteLock.Statement, actorSubject, CreateOperation, idempotencyKey,
            payloadHash, async () =>
            {
                if (revision.RevisionNumber != 1) throw new InvalidOperationException("A draft must begin at revision one.");
                ReviewWriteRules.RequireRevisionAuthor(revision.AuthorSubject, actorSubject);
                revision.ValidateShape();
                await StatementDraftRows.RequireCandidateExtractionAsync(db, revision, cancellationToken);
                var draft = await db.Set<StatementDraftRow>()
                    .SingleOrDefaultAsync(row => row.CandidateId == revision.CandidateId, cancellationToken);
                if (draft is null) db.Add(StatementDraftRows.NewDraft(revision.CandidateId, 1, revision.CreatedAtUtc.UtcTicks));
                else StartRevisionLessDraft(draft);
                db.Add(StatementDraftRows.NewRevision(revision, null));
                return revision;
            }, cancellationToken);
    }

    public async Task<StatementDraftRevision> SaveRevisionAsync(string actorSubject, StatementDraftRevision revision,
        int expectedRevisionNumber, bool statementDateChecked, string idempotencyKey, string payloadHash,
        CancellationToken cancellationToken)
    {
        ReviewIdempotency.ValidateRequest(actorSubject, idempotencyKey, payloadHash);
        ArgumentNullException.ThrowIfNull(revision);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await ReviewIdempotency.WriteOnceAsync(db, ReviewWriteLock.Statement, actorSubject, SaveOperation, idempotencyKey,
            payloadHash, async () =>
            {
                var draft = await ReadDraftAsync(db, revision.CandidateId, cancellationToken);
                ReviewWriteRules.RequireSaveable(draft.CurrentRevisionNumber, expectedRevisionNumber, revision.RevisionNumber);
                ReviewWriteRules.RequireRevisionAuthor(revision.AuthorSubject, actorSubject);
                var current = await ReadRevisionAsync(db, revision.CandidateId, draft.CurrentRevisionNumber, cancellationToken);
                var saved = revision.KeepingAiStatementDateOnlyIfChecked(current, statementDateChecked);
                saved.ValidateShape();
                await StatementDraftRows.RequireCandidateExtractionAsync(db, saved, cancellationToken);
                db.Add(StatementDraftRows.NewRevision(saved, null));
                draft.CurrentRevisionNumber = saved.RevisionNumber;
                draft.ReviewStatus = await ReadStatusAfterSaveAsync(db, draft, cancellationToken);
                return saved;
            }, cancellationToken);
    }

    public async Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, StatementAllowedSelections? approvalSelections, string idempotencyKey, string payloadHash,
        CancellationToken cancellationToken)
    {
        ReviewIdempotency.ValidateRequest(actorSubject, idempotencyKey, payloadHash);
        ReviewWriteRules.RequireDecisionActor(decision, actorSubject);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await ReviewIdempotency.WriteOnceAsync(db, ReviewWriteLock.Statement, actorSubject, DecideOperation, idempotencyKey,
            payloadHash, async () =>
            {
                var draft = await ReadDraftAsync(db, decision.DraftId, cancellationToken);
                ReviewWriteRules.RequireDecidable(draft.CurrentRevisionNumber, draft.ReviewStateVersion, expectedRevisionNumber,
                    expectedReviewStateVersion, decision);
                var review = await ReadReviewAsync(db, draft, cancellationToken);
                ReviewPolicy.ValidateDecision(review.CandidateId, draft.CurrentRevisionNumber, review.ReviewStateVersion, review.Decisions,
                    decision);
                if (decision.Kind == ReviewDecisionKind.Approve)
                    await ValidateApprovalAsync(db, review.CurrentRevision!, approvalSelections, cancellationToken);
                db.Add(new StatementReviewDecisionRow
                {
                    DecisionId = decision.DecisionId,
                    CandidateId = decision.DraftId,
                    RevisionNumber = decision.RevisionNumber,
                    ReviewStateVersion = decision.ReviewStateVersion,
                    DecisionJson = ReviewJson.Serialize(decision),
                    Kind = decision.Kind,
                    CreatedAtUtcTicks = decision.CreatedAtUtc.UtcTicks
                });
                draft.ReviewStateVersion = decision.ReviewStateVersion;
                draft.ReviewStatus = ReviewPolicy.GetCurrentStatus(draft.CurrentRevisionNumber, review.Decisions.Add(decision));
                return decision;
            }, cancellationToken);
    }

    public async Task<StatementReviewRead?> GetAsync(string candidateId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var candidate = await db.Set<StatementCandidateRow>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.CandidateId == candidateId, cancellationToken);
        if (candidate is null) return null;
        var article = await PostgresStatementAnalysisStore.ReadArticleAsync(db, candidateId, cancellationToken)
            ?? throw new InvalidOperationException("The extraction of a statement candidate is missing.");
        var draft = await db.Set<StatementDraftRow>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.CandidateId == candidateId, cancellationToken)
            ?? StatementDraftRows.NewDraft(candidateId, 0, candidate.CreatedAtUtcTicks);
        var revisions = await ReadRevisionRowsAsync(db, [candidateId], cancellationToken);
        var decisions = await ReadDecisionRowsAsync(db, [candidateId], cancellationToken);
        var configurationRevision = await admittingRevisions.ReadAsync(db, candidateId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var review = BuildReview(draft, revisions[candidateId], decisions[candidateId]);
        foreach (var revision in review.Revisions) revision.ValidateAgainst(article.Extraction);
        return new(review, candidate.SourceId, candidate.Url, article.Extraction, revisions[candidateId].FirstOrDefault()?.RunId, configurationRevision);
    }

    public async Task<StatementReviewPage> ListAsync(PageCursor? cursor, int limit, DraftStatusFilter filter,
        CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var fetched = await ReviewKeyset.Page(DraftsWhere(db, filter), cursor, row => row.CreatedAtUtcTicks, row => row.CandidateId)
            .Take(limit + 1).ToArrayAsync(cancellationToken);
        var slice = KeysetSlice<StatementDraftRow>.Create(fetched, limit, cursor, row => (row.CreatedAtUtcTicks, row.CandidateId));
        var ids = slice.Items.Select(row => row.CandidateId).ToArray();
        var candidates = await db.Set<StatementCandidateRow>().AsNoTracking().Where(row => ids.Contains(row.CandidateId))
            .ToDictionaryAsync(row => row.CandidateId, StringComparer.Ordinal, cancellationToken);
        var revisions = await ReadRevisionRowsAsync(db, ids, cancellationToken);
        var decisions = await ReadDecisionRowsAsync(db, ids, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var items = slice.Items.Select(row => ToListItem(row, candidates[row.CandidateId], revisions[row.CandidateId], decisions[row.CandidateId]));
        return new([.. items], slice.NewerCursor, slice.OlderCursor);
    }

    public async Task<StatementNotInReviewPage> ListNotInReviewAsync(PageCursor? cursor, int limit, CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var fetched = await ReviewKeyset.Page(db.Database.SqlQueryRaw<NotInReviewKey>(StatementReviewQueries.NotInReview), cursor,
            key => key.CreatedAtUtcTicks, key => key.CandidateId).Take(limit + 1).ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var slice = KeysetSlice<NotInReviewKey>.Create(fetched, limit, cursor, key => (key.CreatedAtUtcTicks, key.CandidateId));
        var items = slice.Items.Select(key => new StatementNotInReviewItem(key.CandidateId, key.SourceId, key.Url, key.Stage, key.ReasonCode));
        return new([.. items], slice.NewerCursor, slice.OlderCursor);
    }

    private sealed class NotInReviewKey
    {
        public string CandidateId { get; set; } = "";
        public string SourceId { get; set; } = "";
        public string Url { get; set; } = "";
        public long CreatedAtUtcTicks { get; set; }
        public string Stage { get; set; } = "";
        public string ReasonCode { get; set; } = "";
    }

    private static IQueryable<StatementDraftRow> DraftsWhere(CollectionAttemptDbContext db, DraftStatusFilter filter) => filter switch
    {
        DraftStatusFilter.All => db.Set<StatementDraftRow>().AsNoTracking(),
        DraftStatusFilter.NeedsAction => StatementReviewQueries.NeedsActionDrafts(db),
        DraftStatusFilter.Approved => StatementReviewQueries.ApprovedDrafts(db),
        _ => throw new ArgumentOutOfRangeException(nameof(filter))
    };

    private static void StartRevisionLessDraft(StatementDraftRow draft)
    {
        if (draft.CurrentRevisionNumber != 0) throw new StatementDraftExistsException(draft.CandidateId);
        draft.CurrentRevisionNumber = 1;
        draft.ReviewStatus = ReviewPolicy.GetCurrentStatus(1, []);
    }

    private static async Task<StatementDraftRow> ReadDraftAsync(CollectionAttemptDbContext db, string candidateId,
        CancellationToken cancellationToken) =>
        await db.Set<StatementDraftRow>().SingleOrDefaultAsync(row => row.CandidateId == candidateId, cancellationToken)
            ?? throw new KeyNotFoundException("Statement draft was not found.");

    private static async Task ValidateApprovalAsync(CollectionAttemptDbContext db, StatementDraftRevision revision,
        StatementAllowedSelections? allowed, CancellationToken cancellationToken)
    {
        if (allowed is null) throw new ArgumentException("Approval needs the allowed selections.", nameof(allowed));
        revision.ValidateForApproval();
        allowed.RequireAllowed(revision);
        var article = await PostgresStatementAnalysisStore.ReadArticleAsync(db, revision.CandidateId, cancellationToken)
            ?? throw new InvalidOperationException("The extraction of a statement candidate is missing.");
        revision.ValidateAgainst(article.Extraction);
    }

    private static async Task<ReviewStatus> ReadStatusAfterSaveAsync(CollectionAttemptDbContext db, StatementDraftRow draft,
        CancellationToken cancellationToken)
    {
        var decisionRows = await ReadDecisionRowsAsync(db, [draft.CandidateId], cancellationToken);
        return ReviewPolicy.GetCurrentStatus(draft.CurrentRevisionNumber, ParseDecisions(draft, decisionRows[draft.CandidateId]));
    }

    private static async Task<StatementReview> ReadReviewAsync(CollectionAttemptDbContext db, StatementDraftRow draft,
        CancellationToken cancellationToken)
    {
        var revisions = await ReadRevisionRowsAsync(db, [draft.CandidateId], cancellationToken);
        var decisions = await ReadDecisionRowsAsync(db, [draft.CandidateId], cancellationToken);
        return BuildReview(draft, revisions[draft.CandidateId], decisions[draft.CandidateId]);
    }

    private static async Task<ILookup<string, StatementRevisionRow>> ReadRevisionRowsAsync(CollectionAttemptDbContext db,
        string[] candidateIds, CancellationToken cancellationToken)
    {
        var rows = await db.Set<StatementRevisionRow>().AsNoTracking().Where(row => candidateIds.Contains(row.CandidateId))
            .OrderBy(row => row.CandidateId).ThenBy(row => row.RevisionNumber)
            .Take(candidateIds.Length * (ReviewPolicy.MaximumRevisionsPerRecord + 1)).ToArrayAsync(cancellationToken);
        return rows.ToLookup(row => row.CandidateId, StringComparer.Ordinal);
    }

    private static async Task<ILookup<string, StatementReviewDecisionRow>> ReadDecisionRowsAsync(CollectionAttemptDbContext db,
        string[] candidateIds, CancellationToken cancellationToken)
    {
        var rows = await db.Set<StatementReviewDecisionRow>().AsNoTracking().Where(row => candidateIds.Contains(row.CandidateId))
            .OrderBy(row => row.CandidateId).ThenBy(row => row.ReviewStateVersion)
            .Take(candidateIds.Length * (ReviewPolicy.MaximumDecisionsPerRecord + 1)).ToArrayAsync(cancellationToken);
        return rows.ToLookup(row => row.CandidateId, StringComparer.Ordinal);
    }

    private static async Task<StatementDraftRevision> ReadRevisionAsync(CollectionAttemptDbContext db, string candidateId,
        int revisionNumber, CancellationToken cancellationToken)
    {
        var row = await db.Set<StatementRevisionRow>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.CandidateId == candidateId && candidate.RevisionNumber == revisionNumber, cancellationToken)
            ?? throw new InvalidOperationException("Current draft revision is missing.");
        var revision = ReviewJson.Deserialize<StatementDraftRevision>(row.RevisionJson, "revision");
        if (revision.CandidateId != candidateId || revision.RevisionNumber != revisionNumber)
            throw new InvalidOperationException("Stored revision identity is inconsistent.");
        return revision;
    }

    private static StatementReview BuildReview(StatementDraftRow draft, IEnumerable<StatementRevisionRow> revisionRows,
        IEnumerable<StatementReviewDecisionRow> decisionRows)
    {
        var revisions = ParseRevisions(draft, revisionRows);
        var decisions = ParseDecisions(draft, decisionRows);
        ReviewDecisionHistory.Validate(draft.CandidateId, revisions.Length, decisions,
            revisionNumber => revisions[revisionNumber - 1].ValidateForApproval());
        var concerns = ReviewPolicy.GetUnresolvedConcerns(decisions);
        var status = ReviewPolicy.GetCurrentStatus(draft.CurrentRevisionNumber, decisions, concerns);
        return new(draft.CandidateId, revisions.IsEmpty ? null : revisions[^1], draft.ReviewStateVersion, status, revisions, decisions, concerns);
    }

    private static StatementReviewListItem ToListItem(StatementDraftRow draft, StatementCandidateRow candidate,
        IEnumerable<StatementRevisionRow> revisionRows, IEnumerable<StatementReviewDecisionRow> decisionRows)
    {
        var review = BuildReview(draft, revisionRows, decisionRows);
        return new(draft.CandidateId, candidate.SourceId, candidate.Url, review.CurrentRevision, review.ReviewStateVersion,
            review.CurrentStatus, review.UnresolvedConcerns.Length);
    }

    private static ImmutableArray<StatementDraftRevision> ParseRevisions(StatementDraftRow draft, IEnumerable<StatementRevisionRow> rows)
    {
        var revisionRows = rows.ToArray();
        if (revisionRows.Length > ReviewPolicy.MaximumRevisionsPerRecord)
            throw new InvalidOperationException("Stored draft revision history exceeds its limit.");
        var revisions = revisionRows.Select(row => ReviewJson.Deserialize<StatementDraftRevision>(row.RevisionJson, "revision"))
            .ToImmutableArray();
        if (revisions.Length != draft.CurrentRevisionNumber ||
            revisions.Select((revision, index) => revision.RevisionNumber == index + 1 && revision.CandidateId == draft.CandidateId)
                .Any(valid => !valid))
            throw new InvalidOperationException("Stored draft revision history is inconsistent.");
        foreach (var revision in revisions) revision.ValidateShape();
        return revisions;
    }

    private static ImmutableArray<ReviewDecision> ParseDecisions(StatementDraftRow draft, IEnumerable<StatementReviewDecisionRow> rows) =>
        ReviewDecisionHistory.Parse(draft.ReviewStateVersion, rows);
}
