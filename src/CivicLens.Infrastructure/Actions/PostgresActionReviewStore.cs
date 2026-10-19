using System.Collections.Immutable;
using System.Data;
using CivicLens.Application.Actions;
using CivicLens.Application.Briefs;
using CivicLens.Application.Paging;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Actions.Persistence;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.Briefs.Persistence;
using CivicLens.Infrastructure.Relationships;
using CivicLens.Infrastructure.Relationships.Persistence;
using CivicLens.Core.Relationships;
using CivicLens.Application.Relationships;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.BillStream;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Review;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Infrastructure.Actions;

public sealed class PostgresActionReviewStore : IActionReviewStore
{
    internal static readonly ReviewRecords<ActionDraftRow, ActionRevisionRow, ActionReviewDecisionRow, ActionDraftRevision> Records =
        new(PostgresAdvisoryLock.ActionReview, "Action", nameof(ActionDraftRow.RecordId), ActionDraftRows.Revised);

    private const string SaveOperation = "action-save";
    private const string DecideOperation = "action-decide";

    private readonly IDbContextFactory<CollectionAttemptDbContext> contextFactory;
    private readonly PostgresAnalysisQueue<BriefAnalysisRow, BriefAnalysisBudgetRow, BriefDraftRun> briefQueue;
    private readonly PostgresAnalysisQueue<RelationshipAnalysisRow, RelationshipAnalysisBudgetRow, RelationshipDraftRun> relationshipQueue;

    public PostgresActionReviewStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
        : this(contextFactory, new(contextFactory, new BriefDraftQueueMapping()), new(contextFactory, new RelationshipDraftQueueMapping())) { }

    private PostgresActionReviewStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory,
        PostgresAnalysisQueue<BriefAnalysisRow, BriefAnalysisBudgetRow, BriefDraftRun> briefQueue,
        PostgresAnalysisQueue<RelationshipAnalysisRow, RelationshipAnalysisBudgetRow, RelationshipDraftRun> relationshipQueue)
    {
        this.contextFactory = contextFactory;
        this.briefQueue = briefQueue;
        this.relationshipQueue = relationshipQueue;
    }

    public static PostgresActionReviewStore FromConnectionString(string connectionString)
    {
        _ = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
    }

    public async Task<ActionDraftRevision?> GetRevisionAsync(string recordId, int revisionNumber, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await Records.FindRevisionAsync(db, recordId, revisionNumber, cancellationToken);
    }

    public Task<ActionDraftRevision> SaveRevisionAsync(string actorSubject, ActionDraftRevision revision, int expectedRevisionNumber,
        string idempotencyKey, string payloadHash, CancellationToken cancellationToken) =>
        Records.SaveRevisionAsync(contextFactory, actorSubject, revision, expectedRevisionNumber, SaveOperation, idempotencyKey,
            payloadHash, (_, _, current) =>
            {
                revision.ValidatePersonEditOf(current);
                return Task.FromResult(revision);
            }, saved => ActionDraftRows.NewRevision(saved, null), cancellationToken);

    public Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, bool bulkSafe, string idempotencyKey, string payloadHash,
        CancellationToken cancellationToken) =>
        Records.DecideAsync(contextFactory, actorSubject, decision, expectedRevisionNumber, expectedReviewStateVersion, DecideOperation,
            idempotencyKey, payloadHash, async (db, draft, history) =>
            {
                if (bulkSafe && !ToReview(draft, history).IsBulkSafe)
                    throw new ArgumentException("Bulk approval needs a sponsorship-only revision with no open concern.");
                if (decision.Kind != ReviewDecisionKind.Approve) return;
                var current = history.CurrentRevision!;
                current.ValidateForApproval(await ReadExtractionsAsync(db, [current], cancellationToken));
            }, NewDecisionRow, AfterDecisionHooks.InOrder(
                RelationshipStaleness.AfterDecision(relationshipQueue, RelationshipRecordKind.Action),
                BriefStaleness.AfterDecision(briefQueue, PublishedRecordKind.Action)), cancellationToken);

    public async Task<ActionReviewRead?> GetAsync(string recordId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var draft = await db.Set<ActionDraftRow>().AsNoTracking().SingleOrDefaultAsync(row => row.RecordId == recordId, cancellationToken);
        if (draft is null) return null;
        var history = (await Records.ReadHistoriesAsync(db, [draft], cancellationToken))[recordId];
        var extractions = await ReadExtractionsAsync(db, history.LoadedRevisions, cancellationToken);
        var names = await ReadOfficialNamesAsync(db, history.LoadedRevisions, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        foreach (var revision in history.LoadedRevisions) revision.ValidateAgainst(extractions);
        return new(ToReview(draft, history), extractions.ToImmutableDictionary(StringComparer.Ordinal), names);
    }

    public async Task<ActionReviewPage> ListAsync(PageCursor? cursor, int limit, DraftListFilter filter, CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var page = await ReadPageAsync(db, cursor, limit, filter, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return page;
    }

    public async Task<ReviewTriageRead<ActionReview>> ListTriageAsync(PageCursor? cursor, DraftListFilter filter,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var page = await ReadPageAsync(db, cursor, ReviewTriage.PageSize, filter with { Status = DraftStatusFilter.NeedsAction },
            cancellationToken);
        var extractions = await ReadExtractionsAsync(db, page.Items.Select(review => review.CurrentRevision), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(page.Items, extractions.ToImmutableDictionary(StringComparer.Ordinal), page.Priorities, page.NewerCursor, page.OlderCursor);
    }

    private static async Task<ActionReviewPage> ReadPageAsync(CollectionAttemptDbContext db, PageCursor? cursor, int limit,
        DraftListFilter filter, CancellationToken cancellationToken)
    {
        var slice = await ListedDrafts.PageByTierAsync<ActionDraftRow, ActionAnalysisRow>(db, Records.Drafts(db, filter.Status),
            ActionDraftTask.Task, nameof(ActionDraftRow.RecordId), cursor, limit, filter, cancellationToken);
        var drafts = slice.Items.Select(item => item.Draft).ToArray();
        var histories = await Records.ReadHistoriesAsync(db, drafts, cancellationToken);
        return new([.. drafts.Select(row => ToReview(row, histories[row.RecordId]))], ListedDrafts.PrioritiesOf(slice.Items),
            slice.NewerCursor, slice.OlderCursor);
    }

    public async Task<ActionReviewCounts> CountAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var (needsAction, approved) = await Records.CountAsync(db, cancellationToken);
        return new(needsAction, approved);
    }

    public async Task<IReadOnlyList<UnpublishedApprovedAction>> ListUnpublishedApprovedAsync(int limit,
        CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var records = await Records.ReadUnpublishedApprovedAsync(db, ActionReviewQueries.UnpublishedApproved, limit, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return [.. records.Select(record => new UnpublishedApprovedAction(ToReview(record.Draft, record.History), record.PublishedRevisionNumber))];
    }

    public async Task<IReadOnlyDictionary<string, PublishableAction>> GetForPublicationAsync(ImmutableArray<string> recordIds,
        CancellationToken cancellationToken)
    {
        if (recordIds.IsDefaultOrEmpty || recordIds.Length > PublishRelease.MaximumRecords)
            throw new ArgumentOutOfRangeException(nameof(recordIds));
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var drafts = await Records.ReadDraftsAsync(db, [.. recordIds], cancellationToken);
        var histories = await Records.ReadHistoriesAsync(db, drafts, cancellationToken);
        var currents = histories.Values.Select(history => history.CurrentRevision!).ToArray();
        var extractions = (await ReadExtractionsAsync(db, currents, cancellationToken)).ToImmutableDictionary(StringComparer.Ordinal);
        await transaction.CommitAsync(cancellationToken);
        foreach (var current in currents) current.ValidateAgainst(extractions);
        return drafts.ToDictionary(draft => draft.RecordId,
            draft => new PublishableAction(ToReview(draft, histories[draft.RecordId]), extractions), StringComparer.Ordinal);
    }

    private static ActionReview ToReview(ActionDraftRow draft, ReviewHistory<ActionDraftRevision> history) =>
        new(draft.RecordId, history.CurrentRevision!, history.LastApprovedRevision, history.LoadedRevisions, draft.ReviewStateVersion,
            history.CurrentStatus, history.Decisions, history.UnresolvedConcerns);

    private static Task<Dictionary<string, DocumentExtraction>> ReadExtractionsAsync(CollectionAttemptDbContext db,
        IEnumerable<ActionDraftRevision> revisions, CancellationToken cancellationToken) =>
        PostgresDocumentExtractionStore.ReadManyAsync(db,
            [.. revisions.SelectMany(revision => revision.Citations).Select(span => span.ExtractionId).Distinct(StringComparer.Ordinal)],
            cancellationToken);

    private static async Task<ImmutableDictionary<string, string>> ReadOfficialNamesAsync(CollectionAttemptDbContext db,
        IEnumerable<ActionDraftRevision> revisions, CancellationToken cancellationToken)
    {
        var candidateIds = revisions.Select(revision => revision.CandidateId).Distinct(StringComparer.Ordinal).ToArray();
        var revisionIds = await db.Set<ActionCandidateRow>().AsNoTracking().Where(row => candidateIds.Contains(row.CandidateId))
            .ToDictionaryAsync(row => row.CandidateId, row => row.ConfigurationRevisionId, StringComparer.Ordinal, cancellationToken);
        return await ConfiguredPersonNames.ReadAsync(db, candidateIds, revisionIds, cancellationToken);
    }

    private static ActionReviewDecisionRow NewDecisionRow(ReviewDecision decision) => new()
    {
        DecisionId = decision.DecisionId,
        RecordId = decision.DraftId,
        RevisionNumber = decision.RevisionNumber,
        ReviewStateVersion = decision.ReviewStateVersion,
        DecisionJson = ReviewJson.Serialize(decision),
        Kind = decision.Kind,
        CreatedAtUtcTicks = decision.CreatedAtUtc.UtcTicks
    };
}
