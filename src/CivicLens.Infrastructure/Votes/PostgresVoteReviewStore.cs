using System.Collections.Immutable;
using System.Data;
using CivicLens.Application.Briefs;
using CivicLens.Application.Paging;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Application.Votes;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Actions.Persistence;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.Briefs.Persistence;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.HouseVotes;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Votes.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Infrastructure.Votes;

public sealed class PostgresVoteReviewStore : IVoteReviewStore
{
    private const string SaveOperation = "vote-save";
    private const string DecideOperation = "vote-decide";

    private static ReviewRecords<VoteDraftRow, VoteRevisionRow, VoteReviewDecisionRow, VoteDraftRevision> Records => VoteDraftRows.Records;

    private readonly IDbContextFactory<CollectionAttemptDbContext> contextFactory;
    private readonly PostgresAnalysisQueue<BriefAnalysisRow, BriefAnalysisBudgetRow, BriefDraftRun> briefQueue;

    public PostgresVoteReviewStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
        : this(contextFactory, new(contextFactory, new BriefDraftQueueMapping())) { }

    private PostgresVoteReviewStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory,
        PostgresAnalysisQueue<BriefAnalysisRow, BriefAnalysisBudgetRow, BriefDraftRun> briefQueue)
    {
        this.contextFactory = contextFactory;
        this.briefQueue = briefQueue;
    }

    public static PostgresVoteReviewStore FromConnectionString(string connectionString)
    {
        _ = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
    }

    public async Task<VoteDraftRevision?> GetRevisionAsync(string recordId, int revisionNumber, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await Records.FindRevisionAsync(db, recordId, revisionNumber, cancellationToken);
    }

    public Task<VoteDraftRevision> SaveRevisionAsync(string actorSubject, VoteDraftRevision revision, int expectedRevisionNumber,
        string idempotencyKey, string payloadHash, CancellationToken cancellationToken) =>
        Records.SaveRevisionAsync(contextFactory, actorSubject, revision, expectedRevisionNumber, SaveOperation, idempotencyKey,
            payloadHash, (_, current) =>
            {
                revision.ValidatePersonEditOf(current);
                return Task.FromResult(revision);
            }, saved => VoteDraftRows.NewRevision(saved, null), cancellationToken);

    public Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, string idempotencyKey, string payloadHash, CancellationToken cancellationToken) =>
        Records.DecideAsync(contextFactory, actorSubject, decision, expectedRevisionNumber, expectedReviewStateVersion, DecideOperation,
            idempotencyKey, payloadHash, async (db, _, history) =>
            {
                if (decision.Kind != ReviewDecisionKind.Approve) return;
                var current = history.CurrentRevision!;
                current.ValidateForApproval();
                current.ValidateAgainst(await ReadExtractionsAsync(db, [current], cancellationToken));
            }, NewDecisionRow, BriefStaleness.AfterDecision(briefQueue, PublishedRecordKind.Vote), cancellationToken);

    public async Task<VoteReviewRead?> GetAsync(string recordId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var draft = await db.Set<VoteDraftRow>().AsNoTracking().SingleOrDefaultAsync(row => row.RecordId == recordId, cancellationToken);
        if (draft is null) return null;
        var history = (await Records.ReadHistoriesAsync(db, [draft], cancellationToken))[recordId];
        var extractions = await ReadExtractionsAsync(db, history.LoadedRevisions, cancellationToken);
        var names = await ReadPersonNamesAsync(db, history.LoadedRevisions, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        foreach (var revision in history.LoadedRevisions) revision.ValidateAgainst(extractions);
        return new(ToReview(draft, history), extractions.ToImmutableDictionary(StringComparer.Ordinal), names);
    }

    public async Task<VoteReviewPage> ListAsync(PageCursor? cursor, int limit, DraftStatusFilter filter, CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var slice = filter == DraftStatusFilter.NeedsAction
            ? await Records.PageAsync(db, cursor, limit, filter, row => row.LastRevisedAtUtcTicks, cancellationToken)
            : await Records.PageAsync(db, cursor, limit, filter, cancellationToken);
        var histories = await Records.ReadHistoriesAsync(db, slice.Items, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new([.. slice.Items.Select(row => ToReview(row, histories[row.RecordId]))], slice.NewerCursor, slice.OlderCursor);
    }

    public async Task<VoteReviewCounts> CountAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var needsAction = await Records.Drafts(db, DraftStatusFilter.NeedsAction).CountAsync(cancellationToken);
        var approved = await Records.Drafts(db, DraftStatusFilter.Approved).CountAsync(cancellationToken);
        return new(needsAction, approved);
    }

    public async Task<IReadOnlyList<UnpublishedApprovedVote>> ListUnpublishedApprovedAsync(int limit, CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var records = await Records.ReadUnpublishedApprovedAsync(db, VoteReviewQueries.UnpublishedApproved, limit, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return [.. records.Select(record => new UnpublishedApprovedVote(ToReview(record.Draft, record.History), record.PublishedRevisionNumber))];
    }

    public async Task<IReadOnlyDictionary<string, PublishableVote>> GetForPublicationAsync(ImmutableArray<string> recordIds,
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
        var billActions = await ReadBillActionRecordIdsAsync(db, currents, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        foreach (var current in currents) current.ValidateAgainst(extractions);
        return drafts.ToDictionary(draft => draft.RecordId, draft =>
            new PublishableVote(ToReview(draft, histories[draft.RecordId]), extractions,
                billActions.GetValueOrDefault(draft.RecordId, [])), StringComparer.Ordinal);
    }

    private static async Task<Dictionary<string, ImmutableArray<string>>> ReadBillActionRecordIdsAsync(CollectionAttemptDbContext db,
        IEnumerable<VoteDraftRevision> revisions, CancellationToken cancellationToken)
    {
        var withBill = revisions.Where(revision => revision.BillType is not null).ToArray();
        var congresses = withBill.Select(revision => revision.Congress).Distinct().ToArray();
        var billTypes = withBill.Select(revision => revision.BillType!).Distinct().ToArray();
        var billNumbers = withBill.Select(revision => revision.BillNumber!.Value).Distinct().ToArray();
        var actions = await db.Set<ActionDraftRow>().AsNoTracking()
            .Where(row => congresses.Contains(row.Congress) && billTypes.Contains(row.BillType) && billNumbers.Contains(row.BillNumber))
            .Select(row => new { row.RecordId, row.Congress, row.BillType, row.BillNumber }).ToArrayAsync(cancellationToken);
        return withBill.ToDictionary(revision => revision.RecordId, revision =>
            actions.Where(action => action.Congress == revision.Congress && action.BillType == revision.BillType &&
                    action.BillNumber == revision.BillNumber)
                .Select(action => action.RecordId).Order(StringComparer.Ordinal).ToImmutableArray(), StringComparer.Ordinal);
    }

    private static VoteReview ToReview(VoteDraftRow draft, ReviewHistory<VoteDraftRevision> history) =>
        new(draft.RecordId, history.CurrentRevision!, history.LastApprovedRevision, history.LoadedRevisions, draft.ReviewStateVersion,
            history.CurrentStatus, history.Decisions, history.UnresolvedConcerns);

    private static Task<Dictionary<string, DocumentExtraction>> ReadExtractionsAsync(CollectionAttemptDbContext db,
        IEnumerable<VoteDraftRevision> revisions, CancellationToken cancellationToken) =>
        PostgresDocumentExtractionStore.ReadManyAsync(db,
            [.. revisions.SelectMany(revision => revision.Citations).Select(span => span.ExtractionId).Distinct(StringComparer.Ordinal)],
            cancellationToken);

    private static async Task<ImmutableDictionary<string, string>> ReadPersonNamesAsync(CollectionAttemptDbContext db,
        IEnumerable<VoteDraftRevision> revisions, CancellationToken cancellationToken)
    {
        var candidateIds = revisions.Select(revision => revision.CandidateId).Distinct(StringComparer.Ordinal).ToArray();
        var revisionIds = await db.Set<HouseVoteCandidateRow>().AsNoTracking().Where(row => candidateIds.Contains(row.CandidateId))
            .ToDictionaryAsync(row => row.CandidateId, row => row.ConfigurationRevisionId, StringComparer.Ordinal, cancellationToken);
        return await ConfiguredPersonNames.ReadAsync(db, candidateIds, revisionIds, cancellationToken);
    }

    private static VoteReviewDecisionRow NewDecisionRow(ReviewDecision decision) => new()
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
