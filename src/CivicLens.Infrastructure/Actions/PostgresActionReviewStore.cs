using System.Collections.Immutable;
using System.Data;
using CivicLens.Application.Actions;
using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Actions.Persistence;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.BillStream;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Actions;

public sealed class PostgresActionReviewStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IActionReviewStore
{
    internal static readonly ReviewRecords<ActionDraftRow, ActionRevisionRow, ActionReviewDecisionRow, ActionDraftRevision> Records =
        new(PostgresAdvisoryLock.ActionReview, "Action", nameof(ActionDraftRow.RecordId),
            (draft, revision) => draft.LastRevisedAtUtcTicks = revision.CreatedAtUtc.UtcTicks);

    private const string SaveOperation = "action-save";
    private const string DecideOperation = "action-decide";

    public async Task<ActionDraftRevision?> GetRevisionAsync(string recordId, int revisionNumber, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await Records.FindRevisionAsync(db, recordId, revisionNumber, cancellationToken);
    }

    public Task<ActionDraftRevision> SaveRevisionAsync(string actorSubject, ActionDraftRevision revision, int expectedRevisionNumber,
        string idempotencyKey, string payloadHash, CancellationToken cancellationToken) =>
        Records.SaveRevisionAsync(contextFactory, actorSubject, revision, expectedRevisionNumber, SaveOperation, idempotencyKey,
            payloadHash, (_, current) =>
            {
                revision.ValidatePersonEditOf(current);
                return Task.FromResult(revision);
            }, saved => ActionDraftRows.NewRevision(saved, null), cancellationToken);

    public Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, bool sponsorshipOnly, string idempotencyKey, string payloadHash,
        CancellationToken cancellationToken) =>
        Records.DecideAsync(contextFactory, actorSubject, decision, expectedRevisionNumber, expectedReviewStateVersion, DecideOperation,
            idempotencyKey, payloadHash, async (db, draft, history) =>
            {
                if (sponsorshipOnly && !ToReview(draft, history).IsSponsorshipOnly)
                    throw new ArgumentException("Bulk approval needs a revision that changes only sponsorships.");
                if (decision.Kind != ReviewDecisionKind.Approve) return;
                var current = history.CurrentRevision!;
                current.ValidateForApproval();
                current.ValidateAgainst(await ReadExtractionsAsync(db, [current], cancellationToken));
            }, NewDecisionRow, cancellationToken);

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

    public async Task<ActionReviewPage> ListAsync(PageCursor? cursor, int limit, DraftStatusFilter filter, CancellationToken cancellationToken)
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

    public async Task<ActionReviewCounts> CountAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var needsAction = await Records.Drafts(db, DraftStatusFilter.NeedsAction).CountAsync(cancellationToken);
        var approved = await Records.Drafts(db, DraftStatusFilter.Approved).CountAsync(cancellationToken);
        return new(needsAction, approved);
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
        var candidateIds = revisions.Select(revision => revision.CandidateId).ToArray();
        var revisionIds = await db.Set<ActionCandidateRow>().AsNoTracking().Where(row => candidateIds.Contains(row.CandidateId))
            .ToDictionaryAsync(row => row.CandidateId, row => row.ConfigurationRevisionId, StringComparer.Ordinal, cancellationToken);
        var configurations = await StoredJobDefinition.LoadRevisionsAsync(db, revisionIds.Values, cancellationToken);
        return candidateIds.SelectMany(id => StoredJobDefinition.Find(configurations, revisionIds[id]).Configuration.People)
            .DistinctBy(person => person.Id, StringComparer.Ordinal)
            .ToImmutableDictionary(person => person.Id, person => person.Name, StringComparer.Ordinal);
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
