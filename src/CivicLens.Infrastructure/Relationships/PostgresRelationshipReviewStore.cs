using System.Collections.Immutable;
using System.Data;
using CivicLens.Application.Paging;
using CivicLens.Application.Publication;
using CivicLens.Application.Relationships;
using CivicLens.Application.Review;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Relationships.Persistence;
using CivicLens.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Relationships;

public sealed class PostgresRelationshipReviewStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IRelationshipReviewStore
{
    private static readonly ReviewRecords<RelationshipDraftRow, RelationshipRevisionRow, RelationshipReviewDecisionRow, RelationshipRevision> Records =
        RelationshipDraftRows.Records;

    private const string FindCitingSql = """
        SELECT a.pair_id AS "RecordId",
               (a.statement_candidate_id = ANY({2})
                OR a.target_kind = 'Action' AND a.target_record_id = ANY({3})
                OR a.target_kind = 'Vote' AND a.target_record_id = ANY({4})) AS "Cites"
        FROM relationship_analysis a
        WHERE a.task = {0} AND a.pair_id = ANY({1})
        """;

    private const string SaveOperation = "relationship-save";
    private const string DecideOperation = "relationship-decide";

    public async Task<RelationshipRevision?> GetRevisionAsync(string recordId, int revisionNumber, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await Records.FindRevisionAsync(db, recordId, revisionNumber, cancellationToken);
    }

    public Task<RelationshipRevision> SaveRevisionAsync(string actorSubject, RelationshipRevision revision, int expectedRevisionNumber,
        string idempotencyKey, string payloadHash, CancellationToken cancellationToken) =>
        Records.SaveRevisionAsync(contextFactory, actorSubject, revision, expectedRevisionNumber, SaveOperation, idempotencyKey,
            payloadHash, async (db, _, current) =>
            {
                revision.ValidatePersonEditOf(current);
                var (statement, target) = await RelationshipCitedReader.ReadCitedAsync(db, current, cancellationToken);
                RelationshipEditOptions.For(current, statement, target).Require(revision);
                return revision;
            }, saved => RelationshipDraftRows.NewRevision(saved, null), cancellationToken);

    public Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, string idempotencyKey, string payloadHash, CancellationToken cancellationToken) =>
        Records.DecideAsync(contextFactory, actorSubject, decision, expectedRevisionNumber, expectedReviewStateVersion, DecideOperation,
            idempotencyKey, payloadHash, async (db, _, history) =>
            {
                if (decision.Kind != ReviewDecisionKind.Approve) return;
                var current = history.CurrentRevision!;
                current.ValidateForApproval();
                if ((await RelationshipCitedReader.FindChangedAsync(db, [current.Statement, current.Target], cancellationToken)).Count > 0)
                    throw new ArgumentException("Approval needs the cited statement and the cited target to be at their approved revisions.");
            }, (_, _, _, _) => Task.CompletedTask, cancellationToken);

    public async Task<RelationshipReviewRead?> GetAsync(string recordId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var draft = await db.Set<RelationshipDraftRow>().AsNoTracking().SingleOrDefaultAsync(row => row.RecordId == recordId, cancellationToken);
        if (draft is null) return null;
        var history = (await Records.ReadHistoriesAsync(db, [draft], cancellationToken))[recordId];
        var current = history.CurrentRevision!;
        var (statement, target) = await RelationshipCitedReader.ReadCitedAsync(db, current, cancellationToken);
        var changed = await RelationshipCitedReader.FindChangedAsync(db, [current.Statement, current.Target], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(ToReview(draft, history), statement, target, changed.Contains(current.Statement), changed.Contains(current.Target),
            RelationshipEditOptions.For(current, statement, target));
    }

    public async Task<IReadOnlyList<UnpublishedApprovedRelationship>> ListUnpublishedApprovedAsync(int limit,
        IReadOnlyCollection<string> excludedRecordIds, CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var records = await Records.ReadUnpublishedApprovedAsync(db, RelationshipReviewQueries.UnpublishedApproved, limit, excludedRecordIds, cancellationToken);
        var citations = records.SelectMany(record => CitationsOf(record.History.CurrentRevision!)).ToArray();
        var notCurrent = await CitedRevisions.FindNotCurrentInActiveReleaseAsync(db, citations.Select(RelationshipRecordKinds.BindingOf), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return [.. records.Select(record =>
        {
            var current = record.History.CurrentRevision!;
            return new UnpublishedApprovedRelationship(ToReview(record.Draft, record.History), CitesNotCurrent(current, notCurrent));
        })];
    }

    // One ANY batch read, as RelationshipCitedReader.FindChangedAsync does. The pair row holds the cited statement and target of its relationship.
    // A relationship without a pair row cannot say what it cites, so the read fails instead of leaving the relationship out.
    public async Task<IReadOnlySet<string>> FindCitingAsync(ImmutableArray<string> relationshipIds, ImmutableArray<PublishedRecordReference> cited,
        CancellationToken cancellationToken)
    {
        if (relationshipIds.IsEmpty) return new HashSet<string>();
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Database.SqlQueryRaw<CitingRow>(FindCitingSql, RelationshipDraftTask.Task, (object)relationshipIds.ToArray(),
            (object)IdsOf(cited, PublishedRecordKind.Statement), (object)IdsOf(cited, PublishedRecordKind.Action),
            (object)IdsOf(cited, PublishedRecordKind.Vote)).ToArrayAsync(cancellationToken);
        if (rows.Length != relationshipIds.Distinct().Count())
            throw new InvalidDataException("A relationship in the release has no pair row, so its cited records are unknown.");
        return rows.Where(row => row.Cites).Select(row => row.RecordId).ToHashSet(StringComparer.Ordinal);
    }

    public async Task<IReadOnlyDictionary<string, PublishableRelationship>> GetForPublicationAsync(ImmutableArray<string> recordIds,
        ImmutableArray<PublishedRevisionBinding> releaseRecords, CancellationToken cancellationToken)
    {
        if (recordIds.IsDefaultOrEmpty || recordIds.Length > PublishRelease.MaximumRecords)
            throw new ArgumentOutOfRangeException(nameof(recordIds));
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var drafts = await Records.ReadDraftsAsync(db, [.. recordIds], cancellationToken);
        var histories = await Records.ReadHistoriesAsync(db, drafts, cancellationToken);
        var currents = histories.Values.Select(history => history.CurrentRevision!).ToArray();
        var cited = await RelationshipCitedReader.ReadCitedAsync(db, currents, cancellationToken);
        var notCurrent = await CitedRevisions.FindNotCurrentAsync(db, currents.SelectMany(CitationsOf).Select(RelationshipRecordKinds.BindingOf),
            releaseRecords, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return drafts.ToDictionary(draft => draft.RecordId, draft =>
        {
            var current = histories[draft.RecordId].CurrentRevision!;
            return new PublishableRelationship(ToReview(draft, histories[draft.RecordId]), cited[draft.RecordId].Statement,
                cited[draft.RecordId].Target, CitesNotCurrent(current, notCurrent));
        }, StringComparer.Ordinal);
    }

    private static string[] IdsOf(ImmutableArray<PublishedRecordReference> cited, PublishedRecordKind kind) =>
        [.. cited.Where(reference => reference.Kind == kind).Select(reference => reference.RecordId)];

    private static RelationshipCitation[] CitationsOf(RelationshipRevision revision) => [revision.Statement, revision.Target];

    private static bool CitesNotCurrent(RelationshipRevision revision, HashSet<PublishedRevisionBinding> notCurrent) =>
        CitationsOf(revision).Any(citation => notCurrent.Contains(RelationshipRecordKinds.BindingOf(citation)));

    private static RelationshipReview ToReview(RelationshipDraftRow draft, ReviewHistory<RelationshipRevision> history) =>
        new(draft.RecordId, history.CurrentRevision!, history.LoadedRevisions, draft.ReviewStateVersion,
            history.CurrentStatus, history.Decisions, history.UnresolvedConcerns, draft.NoLongerMatches);

    private sealed class CitingRow
    {
        public string RecordId { get; set; } = "";
        public bool Cites { get; set; }
    }
}
