using System.Collections.Immutable;
using System.Data;
using CivicLens.Application.Briefs;
using CivicLens.Application.Paging;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Core.Briefs;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Briefs.Persistence;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Infrastructure.Briefs;

public sealed class PostgresBriefReviewStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IBriefReviewStore
{
    internal static readonly ReviewRecords<BriefDraftRow, BriefRevisionRow, BriefReviewDecisionRow, BriefRevision> Records =
        new(PostgresAdvisoryLock.BriefReview, "Brief", nameof(BriefDraftRow.RecordId),
            (draft, revision) => draft.LastRevisedAtUtcTicks = revision.CreatedAtUtc.UtcTicks);

    private const string SaveOperation = "brief-save";
    private const string DecideOperation = "brief-decide";

    public async Task<BriefRevision?> GetRevisionAsync(string recordId, int revisionNumber, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await Records.FindRevisionAsync(db, recordId, revisionNumber, cancellationToken);
    }

    public Task<BriefRevision> SaveRevisionAsync(string actorSubject, BriefRevision revision, int expectedRevisionNumber,
        string idempotencyKey, string payloadHash, CancellationToken cancellationToken) =>
        Records.SaveRevisionAsync(contextFactory, actorSubject, revision, expectedRevisionNumber, SaveOperation, idempotencyKey,
            payloadHash, (_, _, current) =>
            {
                revision.ValidatePersonEditOf(current);
                return Task.FromResult(revision);
            }, saved => BriefDraftRows.NewRevision(saved, null), cancellationToken);

    public Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, string idempotencyKey, string payloadHash, CancellationToken cancellationToken) =>
        Records.DecideAsync(contextFactory, actorSubject, decision, expectedRevisionNumber, expectedReviewStateVersion, DecideOperation,
            idempotencyKey, payloadHash, async (db, _, history) =>
            {
                if (decision.Kind != ReviewDecisionKind.Approve) return;
                var current = history.CurrentRevision!;
                current.ValidateForApproval();
                if ((await BriefRecordReader.FindChangedInActiveReleaseAsync(db, current.Citations, cancellationToken)).Count > 0)
                    throw new ArgumentException("Approval needs every cited revision to be approved and in the active release.");
            }, NewDecisionRow, (_, _, _, _) => Task.CompletedTask, cancellationToken);

    public async Task<BriefReviewRead?> GetAsync(string recordId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var draft = await db.Set<BriefDraftRow>().AsNoTracking().SingleOrDefaultAsync(row => row.RecordId == recordId, cancellationToken);
        if (draft is null) return null;
        var history = (await Records.ReadHistoriesAsync(db, [draft], cancellationToken))[recordId];
        var current = history.CurrentRevision!;
        var suggested = current.Suggestion?.Citations ?? [];
        var bindings = current.Citations.Concat(suggested).Select(BriefRecordReader.BindingOf).Distinct().ToImmutableArray();
        var records = await BriefRecordReader.ReadExactAsync(db, bindings, cancellationToken);
        var changed = await BriefRecordReader.FindChangedInActiveReleaseAsync(db, current.Citations.Concat(suggested), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(ToReview(draft, history), CitedRecordsOf(current.Citations, records, changed), CitedRecordsOf(suggested, records, changed));
    }

    public async Task<BriefReviewPage> ListAsync(PageCursor? cursor, int limit, DraftStatusFilter filter, CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var slice = filter == DraftStatusFilter.NeedsAction
            ? await Records.PageAsync(db, cursor, limit, filter, row => row.LastRevisedAtUtcTicks, cancellationToken)
            : await Records.PageAsync(db, cursor, limit, filter, cancellationToken);
        var histories = await Records.ReadHistoriesAsync(db, slice.Items, cancellationToken);
        var changed = await BriefRecordReader.FindChangedInActiveReleaseAsync(db,
            histories.Values.SelectMany(history => history.CurrentRevision!.Citations), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new([.. slice.Items.Select(row => new BriefReviewItem(ToReview(row, histories[row.RecordId]),
            histories[row.RecordId].CurrentRevision!.Citations.Any(citation => changed.Contains(BriefRecordReader.BindingOf(citation)))))],
            slice.NewerCursor, slice.OlderCursor);
    }

    public async Task<BriefReviewCounts> CountAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var (needsAction, approved) = await Records.CountAsync(db, cancellationToken);
        return new(needsAction, approved);
    }

    public async Task<IReadOnlyList<UnpublishedApprovedBrief>> ListUnpublishedApprovedAsync(int limit,
        IReadOnlyCollection<string> excludedRecordIds, CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var records = await Records.ReadUnpublishedApprovedAsync(db, BriefReviewQueries.UnpublishedApproved, limit, excludedRecordIds, cancellationToken);
        var changed = await BriefRecordReader.FindChangedInActiveReleaseAsync(db,
            records.SelectMany(record => record.History.CurrentRevision!.Citations), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return [.. records.Select(record => new UnpublishedApprovedBrief(ToReview(record.Draft, record.History), record.PublishedRevisionNumber,
            record.History.CurrentRevision!.Citations.Any(citation => changed.Contains(BriefRecordReader.BindingOf(citation)))))];
    }

    public async Task<IReadOnlyDictionary<string, PublishableBrief>> GetForPublicationAsync(ImmutableArray<string> recordIds,
        ImmutableArray<PublishedRevisionBinding> releaseRecords, CancellationToken cancellationToken)
    {
        if (recordIds.IsDefaultOrEmpty || recordIds.Length > PublishRelease.MaximumRecords)
            throw new ArgumentOutOfRangeException(nameof(recordIds));
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var drafts = await Records.ReadDraftsAsync(db, [.. recordIds], cancellationToken);
        var histories = await Records.ReadHistoriesAsync(db, drafts, cancellationToken);
        var citations = histories.Values.SelectMany(history => history.CurrentRevision!.Citations).ToArray();
        var records = await BriefRecordReader.ReadExactAsync(db, [.. citations.Select(BriefRecordReader.BindingOf).Distinct()], cancellationToken);
        var changed = await BriefRecordReader.FindChangedAsync(db, citations, releaseRecords, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return drafts.ToDictionary(draft => draft.RecordId, draft => new PublishableBrief(ToReview(draft, histories[draft.RecordId]),
            CitedRecordsOf(histories[draft.RecordId].CurrentRevision!.Citations, records, changed)), StringComparer.Ordinal);
    }

    private static ImmutableDictionary<string, BriefCitedRecord> CitedRecordsOf(IEnumerable<BriefCitation> citations,
        ImmutableArray<BriefRecord> records, HashSet<PublishedRevisionBinding> changed) =>
        citations.ToImmutableDictionary(citation => citation.Id, citation => CitedRecordOf(citation, records, changed), StringComparer.Ordinal);

    private static BriefCitedRecord CitedRecordOf(BriefCitation citation, ImmutableArray<BriefRecord> records,
        HashSet<PublishedRevisionBinding> changed)
    {
        var record = records.Single(candidate => candidate.Kind == citation.Kind && candidate.RecordId == citation.RecordId &&
            candidate.RevisionNumber == citation.RevisionNumber);
        return new(record.Headline, record.Summary, changed.Contains(BriefRecordReader.BindingOf(citation)));
    }

    private static BriefReview ToReview(BriefDraftRow draft, ReviewHistory<BriefRevision> history) =>
        new(draft.RecordId, history.CurrentRevision!, history.LoadedRevisions, draft.ReviewStateVersion,
            history.CurrentStatus, history.Decisions, history.UnresolvedConcerns);

    private static BriefReviewDecisionRow NewDecisionRow(ReviewDecision decision) => new()
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
