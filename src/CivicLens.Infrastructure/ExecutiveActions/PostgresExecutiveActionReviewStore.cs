using System.Collections.Immutable;
using System.Data;
using CivicLens.Application.ExecutiveActions;
using CivicLens.Application.Paging;
using CivicLens.Application.Publication;
using CivicLens.Core.Documents;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.ExecutiveActions.Persistence;
using CivicLens.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Infrastructure.ExecutiveActions;

public sealed class PostgresExecutiveActionReviewStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IExecutiveActionReviewStore
{
    private const string DecideOperation = "executive-action-decide";

    private static ReviewRecords<ExecutiveActionDraftRow, ExecutiveActionRevisionRow, ExecutiveActionReviewDecisionRow, ExecutiveActionRevision> Records =>
        ExecutiveActionRows.Records;

    public static PostgresExecutiveActionReviewStore FromConnectionString(string connectionString)
    {
        _ = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
    }

    public Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, string idempotencyKey, string payloadHash, CancellationToken cancellationToken) =>
        Records.DecideAsync(contextFactory, actorSubject, decision, expectedRevisionNumber, expectedReviewStateVersion, DecideOperation,
            idempotencyKey, payloadHash, async (db, _, history) =>
            {
                if (decision.Kind != ReviewDecisionKind.Approve) return;
                await ValidateApprovalAsync(db, history.CurrentRevision!, cancellationToken);
            }, ExecutiveActionRows.NewDecisionRow, static (_, _, _, _) => Task.CompletedTask, cancellationToken);

    public async Task<ExecutiveActionReviewRead?> GetAsync(string recordId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var draft = await db.Set<ExecutiveActionDraftRow>().AsNoTracking().SingleOrDefaultAsync(row => row.RecordId == recordId, cancellationToken);
        if (draft is null) return null;
        var history = (await Records.ReadHistoriesAsync(db, [draft], cancellationToken))[recordId];
        var extractions = await ReadExtractionsAsync(db, history.LoadedRevisions, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        foreach (var revision in history.LoadedRevisions) revision.ValidateAgainst(extractions);
        return new(ToReview(draft, history), extractions.ToImmutableDictionary(StringComparer.Ordinal));
    }

    public async Task<ExecutiveActionsNeedingAction> ListNeedingActionAsync(int limit, CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var (drafts, total) = await Records.ReadNeedingActionAsync(db, limit, row => row.LastRevisedAtUtcTicks, cancellationToken);
        var histories = await Records.ReadHistoriesAsync(db, drafts, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new([.. drafts.Select(row => ToReview(row, histories[row.RecordId]))], total);
    }

    public Task<IReadOnlyList<UnpublishedApprovedExecutiveAction>> ListUnpublishedApprovedAsync(int limit, CancellationToken cancellationToken) =>
        ListUnpublishedAsync(ExecutiveActionReviewQueries.UnpublishedApproved, limit, cancellationToken);

    public Task<IReadOnlyList<UnpublishedApprovedExecutiveAction>> ListCodeApprovedUnpublishedAsync(int limit,
        IReadOnlyCollection<string> excludedRecordIds, CancellationToken cancellationToken) =>
        ListUnpublishedAsync(ExecutiveActionReviewQueries.CodeApprovedUnpublished, limit, cancellationToken, [.. excludedRecordIds]);

    private async Task<IReadOnlyList<UnpublishedApprovedExecutiveAction>> ListUnpublishedAsync(string sql, int limit,
        CancellationToken cancellationToken, string[]? excludedRecordIds = null)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var records = await Records.ReadUnpublishedApprovedAsync(db, sql, limit, cancellationToken, excludedRecordIds);
        await transaction.CommitAsync(cancellationToken);
        return [.. records.Select(record => new UnpublishedApprovedExecutiveAction(ToReview(record.Draft, record.History), record.PublishedRevisionNumber))];
    }

    public async Task<IReadOnlyDictionary<string, PublishableExecutiveAction>> GetForPublicationAsync(ImmutableArray<string> recordIds,
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
            draft => new PublishableExecutiveAction(ToReview(draft, histories[draft.RecordId]), extractions), StringComparer.Ordinal);
    }

    private static ExecutiveActionReview ToReview(ExecutiveActionDraftRow draft, ReviewHistory<ExecutiveActionRevision> history) =>
        new(draft.RecordId, history.CurrentRevision!, history.LastApprovedRevision, history.LoadedRevisions, draft.ReviewStateVersion,
            history.CurrentStatus, history.Decisions, history.UnresolvedConcerns);

    internal static async Task ValidateApprovalAsync(CollectionAttemptDbContext db, ExecutiveActionRevision revision,
        CancellationToken cancellationToken) =>
        revision.ValidateForApproval(await ReadExtractionsAsync(db, [revision], cancellationToken));

    private static Task<Dictionary<string, DocumentExtraction>> ReadExtractionsAsync(CollectionAttemptDbContext db,
        IEnumerable<ExecutiveActionRevision> revisions, CancellationToken cancellationToken) =>
        PostgresDocumentExtractionStore.ReadManyAsync(db,
            [.. revisions.SelectMany(revision => revision.Citations).Select(span => span.ExtractionId).Distinct(StringComparer.Ordinal)],
            cancellationToken);
}
