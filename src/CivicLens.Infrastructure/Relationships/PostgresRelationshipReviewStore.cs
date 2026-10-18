using System.Collections.Immutable;
using System.Data;
using CivicLens.Application.Paging;
using CivicLens.Application.Publication;
using CivicLens.Application.Relationships;
using CivicLens.Application.Review;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Relationships.Persistence;
using CivicLens.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Relationships;

public sealed class PostgresRelationshipReviewStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IRelationshipReviewStore
{
    private static readonly ReviewRecords<RelationshipDraftRow, RelationshipRevisionRow, RelationshipReviewDecisionRow, RelationshipRevision> Records =
        RelationshipDraftRows.Records;

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
            payloadHash, async (db, current) =>
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
            }, NewDecisionRow, (_, _, _, _) => Task.CompletedTask, cancellationToken);

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

    public async Task<RelationshipReviewPage> ListAsync(PageCursor? cursor, int limit, DraftStatusFilter filter, CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var slice = filter == DraftStatusFilter.NeedsAction
            ? await Records.PageAsync(db, cursor, limit, filter, row => row.LastRevisedAtUtcTicks, cancellationToken)
            : await Records.PageAsync(db, cursor, limit, filter, cancellationToken);
        var histories = await Records.ReadHistoriesAsync(db, slice.Items, cancellationToken);
        var citations = histories.Values.SelectMany(history => CitationsOf(history.CurrentRevision!)).ToArray();
        var headlines = await RelationshipCitedReader.ReadHeadlinesAsync(db, citations, cancellationToken);
        var changed = await RelationshipCitedReader.FindChangedAsync(db, citations, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new([.. slice.Items.Select(row =>
        {
            var current = histories[row.RecordId].CurrentRevision!;
            return new RelationshipReviewItem(ToReview(row, histories[row.RecordId]), headlines[current.Statement], headlines[current.Target],
                changed.Contains(current.Statement) || changed.Contains(current.Target));
        })], slice.NewerCursor, slice.OlderCursor);
    }

    public async Task<RelationshipReviewCounts> CountAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var needsAction = await Records.Drafts(db, DraftStatusFilter.NeedsAction).CountAsync(cancellationToken);
        var approved = await Records.Drafts(db, DraftStatusFilter.Approved).CountAsync(cancellationToken);
        return new(needsAction, approved);
    }

    public async Task<IReadOnlyList<UnpublishedApprovedRelationship>> ListUnpublishedApprovedAsync(int limit, CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var records = await Records.ReadUnpublishedApprovedAsync(db, RelationshipReviewQueries.UnpublishedApproved, limit, cancellationToken);
        var citations = records.SelectMany(record => CitationsOf(record.History.CurrentRevision!)).ToArray();
        var headlines = await RelationshipCitedReader.ReadHeadlinesAsync(db, citations, cancellationToken);
        var notCurrent = await CitedRevisions.FindNotCurrentInActiveReleaseAsync(db, citations.Select(RelationshipRecordKinds.BindingOf), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return [.. records.Select(record =>
        {
            var current = record.History.CurrentRevision!;
            return new UnpublishedApprovedRelationship(ToReview(record.Draft, record.History), headlines[current.Statement], headlines[current.Target],
                record.PublishedRevisionNumber, CitesNotCurrent(current, notCurrent));
        })];
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

    private static RelationshipCitation[] CitationsOf(RelationshipRevision revision) => [revision.Statement, revision.Target];

    private static bool CitesNotCurrent(RelationshipRevision revision, HashSet<PublishedRevisionBinding> notCurrent) =>
        CitationsOf(revision).Any(citation => notCurrent.Contains(RelationshipRecordKinds.BindingOf(citation)));

    private static RelationshipReview ToReview(RelationshipDraftRow draft, ReviewHistory<RelationshipRevision> history) =>
        new(draft.RecordId, history.CurrentRevision!, history.LoadedRevisions, draft.ReviewStateVersion,
            history.CurrentStatus, history.Decisions, history.UnresolvedConcerns, draft.NoLongerMatches);

    private static RelationshipReviewDecisionRow NewDecisionRow(ReviewDecision decision) => new()
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
