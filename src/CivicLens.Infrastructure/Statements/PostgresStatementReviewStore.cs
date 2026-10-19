using System.Collections.Immutable;
using System.Data;
using CivicLens.Application.Paging;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Application.Statements;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Core.Relationships;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Statements.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Infrastructure.Statements;

public sealed class PostgresStatementReviewStore : IStatementReviewStore
{
    private static readonly ReviewRecords<StatementDraftRow, StatementRevisionRow, StatementReviewDecisionRow, StatementDraftRevision>
        Records = new(PostgresAdvisoryLock.StatementReview, "Statement", nameof(StatementDraftRow.CandidateId));

    private readonly AdmittingRevisionReader admittingRevisions = new();
    private readonly IDbContextFactory<CollectionAttemptDbContext> contextFactory;
    private readonly Func<CollectionAttemptDbContext, string, int, CancellationToken, Task> afterDecision;

    public PostgresStatementReviewStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
        afterDecision = AfterDecisionHooks.ForRecordKind(contextFactory, RelationshipRecordKind.Statement, PublishedRecordKind.Statement);
    }


    private const string CreateOperation = "statement-create";
    private const string SaveOperation = "statement-save";
    private const string DecideOperation = "statement-decide";

    public static PostgresStatementReviewStore FromConnectionString(string connectionString)
    {
        _ = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
    }

    public Task<StatementDraftRevision> CreateAsync(string actorSubject, StatementDraftRevision revision, string idempotencyKey,
        string payloadHash, CancellationToken cancellationToken) =>
        Records.CreateAsync(contextFactory, actorSubject, revision, CreateOperation, idempotencyKey, payloadHash, async db =>
        {
            revision.ValidateShape();
            var capturedOn = await StatementDraftRows.RequireCandidateExtractionAsync(db, revision, cancellationToken);
            var draft = await db.Set<StatementDraftRow>()
                .SingleOrDefaultAsync(row => row.CandidateId == revision.CandidateId, cancellationToken);
            if (draft is null) db.Add(draft = StatementDraftRows.NewDraft(revision.CandidateId, 1, revision.CreatedAtUtc.UtcTicks));
            else StartRevisionLessDraft(draft);
            StatementDraftRows.SetListing(draft, revision, capturedOn);
            db.Add(StatementDraftRows.NewRevision(revision, null));
            return revision;
        }, cancellationToken);

    public Task<StatementDraftRevision> SaveRevisionAsync(string actorSubject, StatementDraftRevision revision,
        int expectedRevisionNumber, bool statementDateChecked, string idempotencyKey, string payloadHash,
        CancellationToken cancellationToken) =>
        Records.SaveRevisionAsync(contextFactory, actorSubject, revision, expectedRevisionNumber, SaveOperation, idempotencyKey,
            payloadHash, async (db, draft, current) =>
            {
                var saved = revision.KeepingAiStatementDateOnlyIfChecked(current, statementDateChecked);
                saved.ValidateShape();
                StatementDraftRows.SetListing(draft, saved, await StatementDraftRows.RequireCandidateExtractionAsync(db, saved, cancellationToken));
                return saved;
            }, saved => StatementDraftRows.NewRevision(saved, null), cancellationToken);

    public Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, StatementAllowedSelections? approvalSelections, string idempotencyKey, string payloadHash,
        CancellationToken cancellationToken) =>
        Records.DecideAsync(contextFactory, actorSubject, decision, expectedRevisionNumber, expectedReviewStateVersion, DecideOperation,
            idempotencyKey, payloadHash, (db, _, history) => decision.Kind == ReviewDecisionKind.Approve
                ? ValidateApprovalAsync(db, history.CurrentRevision!, approvalSelections, cancellationToken)
                : Task.CompletedTask, NewDecisionRow, afterDecision, cancellationToken);

    public async Task<StatementReviewRead?> GetAsync(string candidateId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var candidate = await db.Set<StatementCandidateRow>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.CandidateId == candidateId, cancellationToken);
        if (candidate is null) return null;
        var article = await StatementAnalysisListing.ReadArticleAsync(db, candidateId, cancellationToken)
            ?? throw new InvalidOperationException("The extraction of a statement candidate is missing.");
        var draft = await db.Set<StatementDraftRow>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.CandidateId == candidateId, cancellationToken)
            ?? StatementDraftRows.NewDraft(candidateId, 0, candidate.CreatedAtUtcTicks);
        var history = (await Records.ReadHistoriesAsync(db, [draft], cancellationToken))[candidateId];
        var firstRevisions = await Records.ReadFirstRevisionsAsync(db, [candidateId], cancellationToken);
        var configurationRevision = await admittingRevisions.ReadAsync(db, candidateId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        foreach (var revision in history.LoadedRevisions) revision.ValidateAgainst(article.Extraction);
        var analysisRunId = firstRevisions.TryGetValue(candidateId, out var first) ? first.Row.RunId : null;
        return new(ToReview(draft, history), candidate.SourceId, candidate.Url, article.Extraction, analysisRunId, configurationRevision);
    }

    public async Task<StatementReviewPage> ListAsync(PageCursor? cursor, int limit, DraftListFilter filter,
        CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var (page, _) = await ReadPageAsync(db, Records.Drafts(db, filter.Status), cursor, limit, filter, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return page;
    }

    public async Task<StatementTriageRead> ListTriageAsync(PageCursor? cursor, DraftListFilter filter, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var drafted = Records.Drafts(db, DraftStatusFilter.NeedsAction).Where(draft => draft.CurrentRevisionNumber > 0);
        var (page, candidates) = await ReadPageAsync(db, drafted, cursor, ReviewTriage.PageSize, filter, cancellationToken);
        var candidateIds = page.Items.Select(item => item.CandidateId).ToArray();
        var extractions = await PostgresDocumentExtractionStore.ReadManyAsync(db,
            [.. candidates.Values.Select(candidate => candidate.ExtractionId).Distinct(StringComparer.Ordinal)], cancellationToken);
        var configurationRevisions = await admittingRevisions.ReadManyAsync(db, candidateIds, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var items = page.Items.Select(item => new StatementTriageEvidence(item, extractions[candidates[item.CandidateId].ExtractionId],
            configurationRevisions.GetValueOrDefault(item.CandidateId)));
        return new([.. items], page.NewerCursor, page.OlderCursor);
    }

    private async Task<(StatementReviewPage Page, Dictionary<string, StatementCandidateRow> Candidates)> ReadPageAsync(CollectionAttemptDbContext db, IQueryable<StatementDraftRow> drafts,
        PageCursor? cursor, int limit, DraftListFilter filter, CancellationToken cancellationToken)
    {
        var slice = await ListedDrafts.PageWithoutTierAsync(drafts, nameof(StatementDraftRow.CandidateId), cursor, limit, filter,
            cancellationToken);
        var rows = slice.Items.Select(item => item.Draft).ToArray();
        var candidates = await ReadCandidatesAsync(db, [.. rows.Select(row => row.CandidateId)], cancellationToken);
        var histories = await Records.ReadHistoriesAsync(db, rows, cancellationToken);
        var items = rows.Select(row => ToListItem(row, candidates[row.CandidateId], histories[row.CandidateId]));
        return (new([.. items], slice.NewerCursor, slice.OlderCursor), candidates);
    }

    public async Task<StatementReviewCounts> CountAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var (needsAction, approved) = await Records.CountAsync(db, cancellationToken);
        return new(needsAction, approved);
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

    public async Task<IReadOnlyList<UnpublishedApprovedStatement>> ListUnpublishedApprovedAsync(int limit,
        CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var records = await Records.ReadUnpublishedApprovedAsync(db, StatementReviewQueries.UnpublishedApproved, limit, cancellationToken);
        var candidates = await ReadCandidatesAsync(db, [.. records.Select(record => record.Draft.CandidateId)], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return [.. records.Select(record => new UnpublishedApprovedStatement(
            ToListItem(record.Draft, candidates[record.Draft.CandidateId], record.History), record.PublishedRevisionNumber))];
    }

    public async Task<IReadOnlyDictionary<string, PublishableStatement>> GetForPublicationAsync(ImmutableArray<string> candidateIds,
        CancellationToken cancellationToken)
    {
        if (candidateIds.IsDefaultOrEmpty || candidateIds.Length > PublishRelease.MaximumRecords)
            throw new ArgumentOutOfRangeException(nameof(candidateIds));
        var ids = candidateIds.ToArray();
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var drafts = await Records.ReadDraftsAsync(db, ids, cancellationToken);
        var candidates = await ReadCandidatesAsync(db, ids, cancellationToken);
        var histories = await Records.ReadHistoriesAsync(db, drafts, cancellationToken);
        var extractions = await PostgresDocumentExtractionStore.ReadManyAsync(db,
            [.. candidates.Values.Select(candidate => candidate.ExtractionId).Distinct(StringComparer.Ordinal)], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return drafts.ToDictionary(draft => draft.CandidateId, draft => ToPublishable(draft, candidates[draft.CandidateId],
            extractions[candidates[draft.CandidateId].ExtractionId], histories[draft.CandidateId]), StringComparer.Ordinal);
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

    private static void StartRevisionLessDraft(StatementDraftRow draft)
    {
        if (draft.CurrentRevisionNumber != 0) throw new StatementDraftExistsException(draft.CandidateId);
        draft.CurrentRevisionNumber = 1;
        draft.ReviewStatus = ReviewPolicy.GetInitialStatus(1);
    }

    private static async Task ValidateApprovalAsync(CollectionAttemptDbContext db, StatementDraftRevision revision,
        StatementAllowedSelections? allowed, CancellationToken cancellationToken)
    {
        if (allowed is null) throw new ArgumentException("Approval needs the allowed selections.", nameof(allowed));
        var article = await StatementAnalysisListing.ReadArticleAsync(db, revision.CandidateId, cancellationToken)
            ?? throw new InvalidOperationException("The extraction of a statement candidate is missing.");
        allowed.RequireApprovable(revision, article.Extraction);
    }

    private static StatementReviewDecisionRow NewDecisionRow(ReviewDecision decision) => new()
    {
        DecisionId = decision.DecisionId,
        CandidateId = decision.DraftId,
        RevisionNumber = decision.RevisionNumber,
        ReviewStateVersion = decision.ReviewStateVersion,
        DecisionJson = ReviewJson.Serialize(decision),
        Kind = decision.Kind,
        CreatedAtUtcTicks = decision.CreatedAtUtc.UtcTicks
    };

    private static Task<Dictionary<string, StatementCandidateRow>> ReadCandidatesAsync(CollectionAttemptDbContext db,
        string[] candidateIds, CancellationToken cancellationToken) =>
        db.Set<StatementCandidateRow>().AsNoTracking().Where(row => candidateIds.Contains(row.CandidateId))
            .ToDictionaryAsync(row => row.CandidateId, StringComparer.Ordinal, cancellationToken);

    private static StatementReview ToReview(StatementDraftRow draft, ReviewHistory<StatementDraftRevision> history) =>
        new(draft.CandidateId, history.CurrentRevision, draft.ReviewStateVersion, history.CurrentStatus, history.LoadedRevisions,
            history.Decisions, history.UnresolvedConcerns);

    private static PublishableStatement ToPublishable(StatementDraftRow draft, StatementCandidateRow candidate,
        DocumentExtraction extraction, ReviewHistory<StatementDraftRevision> history)
    {
        history.CurrentRevision?.ValidateAgainst(extraction);
        return new(ToReview(draft, history), candidate.Url, extraction);
    }

    private static StatementReviewListItem ToListItem(StatementDraftRow draft, StatementCandidateRow candidate,
        ReviewHistory<StatementDraftRevision> history) =>
        new(draft.CandidateId, candidate.SourceId, candidate.Url, history.CurrentRevision, draft.ReviewStateVersion,
            history.CurrentStatus, history.UnresolvedConcerns.Length);
}
