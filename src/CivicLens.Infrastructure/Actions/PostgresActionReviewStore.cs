using System.Collections.Immutable;
using System.Data;
using CivicLens.Application;
using CivicLens.Application.Actions;
using CivicLens.Application.Paging;
using CivicLens.Application.Publication;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Actions.Persistence;
using CivicLens.Core.Relationships;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.BillStream;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Votes;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Infrastructure.Actions;

public sealed class PostgresActionReviewStore : IActionReviewStore
{
    internal static readonly ReviewRecords<ActionDraftRow, ActionRevisionRow, ActionReviewDecisionRow, ActionDraftRevision> Records =
        new(PostgresAdvisoryLock.ActionReview, PublishedRecordKind.Action, "Action", nameof(ActionDraftRow.RecordId), ActionRevisionRows.ReadAsync,
            ActionDraftRows.Revised, new(ActionDraftRows.RequireNoRefusedUpdateAsync));

    private const string SaveOperation = "action-save";
    private const string DecideOperation = "action-decide";

    private readonly IDbContextFactory<CollectionAttemptDbContext> contextFactory;
    private readonly Func<CollectionAttemptDbContext, string, int, CancellationToken, Task> afterDecision;

    public PostgresActionReviewStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
        afterDecision = AfterDecisionHooks.ForRecordKind(contextFactory, RelationshipRecordKind.Action, PublishedRecordKind.Action);
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
        ReviewDecision? approval, string idempotencyKey, string payloadHash, ImmutableArray<Guid> correctedReportIds,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, DocumentExtraction>? extractions = null;
        return Records.SaveRevisionAsync(contextFactory, actorSubject, revision, expectedRevisionNumber, SaveOperation, idempotencyKey,
            payloadHash, async (db, _, current) =>
            {
                revision.ValidatePersonEditOf(current);
                extractions = revision.SummaryCitation is null && approval is null
                    ? ImmutableDictionary<string, DocumentExtraction>.Empty
                    : await ReadExtractionsAsync(db, [revision], cancellationToken);
                return revision;
            }, saved =>
            {
                var row = ActionRevisionRows.Create(saved, null, CodeVersion.Unknown);
                ActionRevisionRows.FillCrsSummary(row, saved, extractions!);
                return row;
            }, null,
            approval is null ? null : new(approval, (db, _, history) => ValidateApprovalAsync(db, history.CurrentRevision!, cancellationToken, extractions),
                afterDecision, revision => revision.ComparableForm(), correctedReportIds), cancellationToken);
    }

    public Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, string idempotencyKey, string payloadHash,
        CancellationToken cancellationToken) =>
        Records.DecideAsync(contextFactory, actorSubject, decision, expectedRevisionNumber, expectedReviewStateVersion, DecideOperation,
            idempotencyKey, payloadHash, async (db, _, history) =>
            {
                if (decision.Kind != ReviewDecisionKind.Approve) return;
                var current = history.CurrentRevision!;
                await ValidateApprovalAsync(db, current, cancellationToken);
            }, afterDecision, [], cancellationToken);

    public async Task<string?> FindRecordIdByBillAsync(int congress, string billType, int billNumber, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Set<ActionDraftRow>().AsNoTracking()
            .Where(row => row.Congress == congress && row.BillType == billType && row.BillNumber == billNumber)
            .OrderBy(row => row.RecordId).Select(row => row.RecordId).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ActionReviewRead?> GetAsync(string recordId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var draft = await db.Set<ActionDraftRow>().AsNoTracking().SingleOrDefaultAsync(row => row.RecordId == recordId, cancellationToken);
        if (draft is null) return null;
        var history = (await Records.ReadHistoriesAsync(db, [draft], cancellationToken))[recordId];
        var extractions = await ReadExtractionsAsync(db, history.LoadedRevisions, cancellationToken);
        var names = await ReadOfficialNamesAsync(db, history.LoadedRevisions, cancellationToken);
        var billVotes = await BillVoteDrafts.ReadAsync(db, draft.Congress, draft.BillType, draft.BillNumber, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        foreach (var revision in history.LoadedRevisions) revision.ValidateAgainst(extractions);
        return new(ToReview(draft, history), extractions.ToImmutableDictionary(StringComparer.Ordinal), names, billVotes);
    }

    public async Task<IReadOnlyList<UnpublishedApprovedAction>> ListUnpublishedApprovedAsync(int limit,
        IReadOnlyCollection<string> excludedRecordIds, CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var records = await Records.ReadUnpublishedApprovedAsync(db, ActionReviewQueries.UnpublishedApproved, limit, excludedRecordIds, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return [.. records.Select(record => new UnpublishedApprovedAction(ToReview(record.Draft, record.History)))];
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
        var textVersions = await ReadTextVersionsAsync(db, currents, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        foreach (var current in currents) current.ValidateAgainst(extractions);
        return drafts.ToDictionary(draft => draft.RecordId,
            draft => new PublishableAction(ToReview(draft, histories[draft.RecordId]), extractions, textVersions.GetValueOrDefault(draft.RecordId)),
            StringComparer.Ordinal);
    }

    /// <summary>One bounded read of the stored bill text rows that the text drafts cite, by record ID. A revision with no bill text has no entry.</summary>
    private static async Task<Dictionary<string, ActionTextVersion>> ReadTextVersionsAsync(CollectionAttemptDbContext db,
        IEnumerable<ActionDraftRevision> revisions, CancellationToken cancellationToken)
    {
        var texts = revisions.Where(revision => revision.TextPackageId is not null).ToArray();
        if (texts.Length == 0) return new(StringComparer.Ordinal);
        var packageIds = texts.Select(revision => revision.TextPackageId!).Distinct(StringComparer.Ordinal).ToArray();
        var rows = await db.Set<BillTextVersionRow>().AsNoTracking().Where(row => packageIds.Contains(row.PackageId)).ToArrayAsync(cancellationToken);
        return texts.ToDictionary(revision => revision.RecordId, revision =>
        {
            var row = rows.Single(row => row.Congress == revision.Congress && row.BillType == revision.BillType &&
                row.BillNumber == revision.BillNumber && row.PackageId == revision.TextPackageId);
            if (revision.TextCitations.Any(citation => citation.Span.ExtractionId != row.ExtractionId))
                throw new InvalidDataException("Bill text citations do not name the extraction of their stored text version.");
            return new ActionTextVersion(row.PackageId, row.VersionType, row.VersionDate, row.CharsRead, row.CharsTotal);
        }, StringComparer.Ordinal);
    }

    private static ActionReview ToReview(ActionDraftRow draft, ReviewHistory<ActionDraftRevision> history) =>
        new(draft.RecordId, history.CurrentRevision!, history.LastApprovedRevision, history.LoadedRevisions, draft.ReviewStateVersion,
            history.CurrentStatus, history.Decisions, history.UnresolvedConcerns);

    internal static async Task ValidateApprovalAsync(CollectionAttemptDbContext db, ActionDraftRevision revision, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, DocumentExtraction>? extractions = null) =>
        ActionTextApproval.Validate(revision, extractions ?? await ReadExtractionsAsync(db, [revision], cancellationToken));

    internal static Task<Dictionary<string, DocumentExtraction>> ReadExtractionsAsync(CollectionAttemptDbContext db,
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
}
