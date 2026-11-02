using System.Collections.Immutable;
using System.Data;
using CivicLens.Application.Paging;
using CivicLens.Application.Publication;
using CivicLens.Application.Votes;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Actions.Persistence;
using CivicLens.Core.Relationships;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Votes;
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
    private readonly Func<CollectionAttemptDbContext, string, int, CancellationToken, Task> afterDecision;

    public PostgresVoteReviewStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
        afterDecision = AfterDecisionHooks.ForRecordKind(contextFactory, RelationshipRecordKind.Vote, PublishedRecordKind.Vote);
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
            payloadHash, (_, _, current) =>
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
                await ValidateApprovalAsync(db, current, cancellationToken);
            }, afterDecision, cancellationToken);

    public async Task<VoteReviewRead?> GetAsync(string recordId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var draft = await db.Set<VoteDraftRow>().AsNoTracking().SingleOrDefaultAsync(row => row.RecordId == recordId, cancellationToken);
        if (draft is null) return null;
        var history = (await Records.ReadHistoriesAsync(db, [draft], cancellationToken))[recordId];
        var extractions = await ReadExtractionsAsync(db, history.LoadedRevisions, cancellationToken);
        var names = await ReadPersonNamesAsync(db, history.LoadedRevisions, cancellationToken);
        var billActions = await ReadBillActionRecordIdsAsync(db, VoteBill.Of(history.CurrentRevision!) is { } bill ? [bill] : [], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        foreach (var revision in history.LoadedRevisions) revision.ValidateAgainst(extractions);
        return new(ToReview(draft, history), extractions.ToImmutableDictionary(StringComparer.Ordinal), names,
            billActions.GetValueOrDefault(recordId, []));
    }

    public async Task<IReadOnlyList<UnpublishedApprovedVote>> ListUnpublishedApprovedAsync(int limit,
        IReadOnlyCollection<string> excludedRecordIds, CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var records = await Records.ReadUnpublishedApprovedAsync(db, VoteReviewQueries.UnpublishedApproved, limit, excludedRecordIds, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return [.. records.Select(record => new UnpublishedApprovedVote(ToReview(record.Draft, record.History)))];
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
        var billActions = await ReadBillActionRecordIdsAsync(db, [.. currents.Select(VoteBill.Of).OfType<VoteBill>()], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        foreach (var current in currents) current.ValidateAgainst(extractions);
        return drafts.ToDictionary(draft => draft.RecordId, draft =>
            new PublishableVote(ToReview(draft, histories[draft.RecordId]), extractions,
                billActions.GetValueOrDefault(draft.RecordId, [])), StringComparer.Ordinal);
    }

    public async Task<string?> FindRecordIdByRollAsync(Chamber chamber, int congress, int sessionNumber, int rollCallNumber,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Set<VoteDraftRow>().AsNoTracking()
            .Where(row => row.Chamber == chamber && row.Congress == congress && row.SessionNumber == sessionNumber &&
                row.RollCallNumber == rollCallNumber)
            .OrderBy(row => row.RecordId).Select(row => row.RecordId).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VoteMembership>> ReadMembersAsync(ImmutableArray<PublishedRevisionBinding> revisions,
        CancellationToken cancellationToken)
    {
        if (revisions.IsEmpty) return [];
        var requested = revisions.Distinct().ToArray();
        CitedRevisions.RequireKind(PublishedRecordKind.Vote, requested, nameof(revisions));
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Database.SqlQueryRaw<StoredVoteMember>("""
                SELECT k.id AS "VoteRecordId", r.record_id IS NOT NULL AS "IsStored", m.person_id AS "PersonId"
                FROM unnest({0}, {1}) AS k(id, number)
                LEFT JOIN vote_revisions r ON r.record_id = k.id AND r.revision_number = k.number
                LEFT JOIN LATERAL (SELECT DISTINCT e ->> 'personId' AS person_id
                                   FROM jsonb_array_elements(r.revision_json::jsonb -> 'members') e) m ON true
                """, requested.Select(binding => binding.RecordId).ToArray(), requested.Select(binding => binding.RevisionNumber).ToArray())
            .ToArrayAsync(cancellationToken);
        if (rows.Any(row => !row.IsStored)) throw CitedRevisions.NotStored(PublishedRecordKind.Vote);
        return [.. rows.Select(row => new VoteMembership(row.VoteRecordId,
            row.PersonId ?? throw new InvalidDataException("A stored vote revision names no member.")))];
    }

    public async Task<IReadOnlyDictionary<string, ImmutableArray<string>>> FindBillActionRecordIdsOfVotesAsync(
        ImmutableArray<PublishedRevisionBinding> revisions, CancellationToken cancellationToken)
    {
        if (revisions.IsEmpty) return ImmutableDictionary<string, ImmutableArray<string>>.Empty;
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var requested = revisions.Distinct().ToArray();
        CitedRevisions.RequireKind(PublishedRecordKind.Vote, requested, nameof(revisions));
        var stored = await db.Database.SqlQueryRaw<StoredVoteBill>("""
                SELECT r.record_id AS "VoteRecordId", (r.revision_json::jsonb ->> 'congress')::int AS "Congress",
                       r.revision_json::jsonb ->> 'billType' AS "BillType", (r.revision_json::jsonb ->> 'billNumber')::int AS "BillNumber"
                FROM vote_revisions r JOIN unnest({0}, {1}) AS k(id, number) ON r.record_id = k.id AND r.revision_number = k.number
                """, requested.Select(binding => binding.RecordId).ToArray(), requested.Select(binding => binding.RevisionNumber).ToArray())
            .ToArrayAsync(cancellationToken);
        if (stored.Length != requested.Length) throw CitedRevisions.NotStored(PublishedRecordKind.Vote);
        var bills = stored.Select(row => VoteBill.Of(row.VoteRecordId, row.Congress, row.BillType, row.BillNumber)).OfType<VoteBill>();
        return await ReadBillActionRecordIdsAsync(db, [.. bills], cancellationToken);
    }

    private static async Task<Dictionary<string, ImmutableArray<string>>> ReadBillActionRecordIdsAsync(CollectionAttemptDbContext db,
        ImmutableArray<VoteBill> bills, CancellationToken cancellationToken)
    {
        if (bills.IsEmpty) return [];
        var keys = bills.Select(bill => (bill.Congress, bill.BillType, bill.BillNumber)).Distinct().ToArray();
        var actions = await db.Set<ActionDraftRow>().FromSqlRaw("""
                SELECT d.* FROM action_drafts d
                JOIN unnest({0}, {1}, {2}) AS k(congress, bill_type, bill_number)
                  ON d.congress = k.congress AND d.bill_type = k.bill_type AND d.bill_number = k.bill_number
                """,
                keys.Select(key => key.Congress).ToArray(), keys.Select(key => key.BillType).ToArray(),
                keys.Select(key => key.BillNumber).ToArray())
            .AsNoTracking().Select(row => new { row.RecordId, row.Congress, row.BillType, row.BillNumber }).ToArrayAsync(cancellationToken);
        var byBill = actions.ToLookup(action => (action.Congress, action.BillType, action.BillNumber));
        return bills.ToDictionary(bill => bill.VoteRecordId, bill =>
            byBill[(bill.Congress, bill.BillType, bill.BillNumber)].Select(action => action.RecordId).Order(StringComparer.Ordinal).ToImmutableArray(),
            StringComparer.Ordinal);
    }

    private static VoteReview ToReview(VoteDraftRow draft, ReviewHistory<VoteDraftRevision> history) =>
        new(draft.RecordId, history.CurrentRevision!, history.LastApprovedRevision, history.LoadedRevisions, draft.ReviewStateVersion,
            history.CurrentStatus, history.Decisions, history.UnresolvedConcerns);

    internal static async Task ValidateApprovalAsync(CollectionAttemptDbContext db, VoteDraftRevision revision, CancellationToken cancellationToken) =>
        revision.ValidateForApproval(await ReadExtractionsAsync(db, [revision], cancellationToken));

    private static Task<Dictionary<string, DocumentExtraction>> ReadExtractionsAsync(CollectionAttemptDbContext db,
        IEnumerable<VoteDraftRevision> revisions, CancellationToken cancellationToken) =>
        PostgresDocumentExtractionStore.ReadManyAsync(db,
            [.. revisions.SelectMany(revision => revision.Citations).Select(span => span.ExtractionId).Distinct(StringComparer.Ordinal)],
            cancellationToken);

    private static async Task<ImmutableDictionary<string, string>> ReadPersonNamesAsync(CollectionAttemptDbContext db,
        IEnumerable<VoteDraftRevision> revisions, CancellationToken cancellationToken)
    {
        var candidateIds = revisions.Select(revision => revision.CandidateId).Distinct(StringComparer.Ordinal).ToArray();
        var revisionIds = await db.Set<VoteCandidateRow>().AsNoTracking().Where(row => candidateIds.Contains(row.CandidateId))
            .ToDictionaryAsync(row => row.CandidateId, row => row.ConfigurationRevisionId, StringComparer.Ordinal, cancellationToken);
        return await ConfiguredPersonNames.ReadAsync(db, candidateIds, revisionIds, cancellationToken);
    }

    private sealed record StoredVoteMember(string VoteRecordId, bool IsStored, string? PersonId);

    private sealed record StoredVoteBill(string VoteRecordId, int Congress, string? BillType, int? BillNumber);
}
