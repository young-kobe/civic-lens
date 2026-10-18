using System.Collections.Immutable;
using System.Data;
using System.Text.Json;
using CivicLens.Application.Briefs;
using CivicLens.Application.Paging;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.Briefs.Persistence;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Documents.Persistence;
using CivicLens.Infrastructure.Review.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Infrastructure.Review;

/// <summary>Transactional persistence for immutable document-change revisions and review decisions.</summary>
public sealed class PostgresDocumentChangeReviewStore : IDocumentChangeReviewStore
{
    internal static readonly ReviewRecords<DocumentChangeDraftRow, DocumentChangeRevisionRow, ReviewDecisionRow, DocumentChangeDraftRevision>
        Records = new(PostgresAdvisoryLock.DocumentChangeReview, "Document-change", nameof(DocumentChangeDraftRow.DraftId));

    private const string CreateOperation = "create";
    private const string SaveOperation = "save";
    private const string DecideOperation = "decide";

    private readonly IDbContextFactory<CollectionAttemptDbContext> contextFactory;
    private readonly PostgresAnalysisQueue<BriefAnalysisRow, BriefAnalysisBudgetRow, BriefDraftRun> briefQueue;

    public PostgresDocumentChangeReviewStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
        : this(contextFactory, new(contextFactory, new BriefDraftQueueMapping())) { }

    private PostgresDocumentChangeReviewStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory,
        PostgresAnalysisQueue<BriefAnalysisRow, BriefAnalysisBudgetRow, BriefDraftRun> briefQueue)
    {
        this.contextFactory = contextFactory;
        this.briefQueue = briefQueue;
    }

    public static PostgresDocumentChangeReviewStore FromConnectionString(string connectionString)
    {
        _ = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
    }

    public Task<DocumentChangeDraftRevision> CreateAsync(string actorSubject,
        DocumentChangeDraftRevision revision, string idempotencyKey, string payloadHash,
        CancellationToken cancellationToken) =>
        Records.CreateAsync(contextFactory, actorSubject, revision, CreateOperation, idempotencyKey, payloadHash, async db =>
        {
            var existingDraftId = await ReadDraftIdForComparisonAsync(db, revision.ComparisonId, cancellationToken);
            if (existingDraftId is not null) throw new DocumentChangeDraftExistsException(existingDraftId);
            var verified = await ReadVerifiedComparisonAsync(db, revision.ComparisonId, cancellationToken)
                ?? throw new ArgumentException("Drafts must reference a complete comparison containing a text change.", nameof(revision));
            var initial = revision with { Citations = CreateInitialCitations(verified.Summary.Comparison) };
            ValidateRevisionAgainstEvidence(initial, verified);
            AddDraft(db, initial);
            return initial;
        }, cancellationToken);

    public Task<DocumentChangeDraftRevision> SaveRevisionAsync(string actorSubject,
        DocumentChangeDraftRevision revision, int expectedRevisionNumber, bool changeDateChecked, string idempotencyKey,
        string payloadHash, CancellationToken cancellationToken) =>
        Records.SaveRevisionAsync(contextFactory, actorSubject, revision, expectedRevisionNumber, SaveOperation, idempotencyKey,
            payloadHash, async (db, _, current) =>
            {
                var saved = revision.KeepingAiChangeDateOnlyIfChecked(current, changeDateChecked) with { ComparisonId = current.ComparisonId };
                await ValidateRevisionAgainstEvidenceAsync(db, saved, cancellationToken);
                return saved;
            }, NewRevisionRow, cancellationToken);

    public Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision,
        int expectedRevisionNumber, int expectedReviewStateVersion, string idempotencyKey,
        string payloadHash, ImmutableHashSet<string> allowedOfficialIds, ImmutableHashSet<string> allowedIssueIds,
        CancellationToken cancellationToken) =>
        Records.DecideAsync(contextFactory, actorSubject, decision, expectedRevisionNumber, expectedReviewStateVersion, DecideOperation,
            idempotencyKey, payloadHash, async (db, draft, history) =>
            {
                var comparisons = await ReadVerifiedComparisonsAsync(db, [draft.ComparisonId], cancellationToken);
                ValidateAgainstEvidence(draft, history, comparisons);
                if (decision.Kind != ReviewDecisionKind.Approve) return;
                var current = history.CurrentRevision!;
                if (current.OfficialIds.Any(id => !allowedOfficialIds.Contains(id)) || current.IssueIds.Any(id => !allowedIssueIds.Contains(id)))
                    throw new ArgumentException("The saved revision includes selections that are no longer configured.");
                current.ValidateForApproval();
            }, NewDecisionRow, BriefStaleness.AfterDecision(briefQueue, PublishedRecordKind.DocumentChange), cancellationToken);

    public async Task<DocumentChangeReview?> GetAsync(string draftId, CancellationToken cancellationToken)
    {
        DocumentChangeDraftRevision.ValidateDraftId(draftId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var draft = await db.Set<DocumentChangeDraftRow>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.DraftId == draftId, cancellationToken);
        if (draft is null) return null;
        var result = (await ReadAggregatesAsync(db, [draft], cancellationToken))[draftId].Review;
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<IReadOnlyDictionary<string, PublishableDocumentChange>> GetForPublicationAsync(
        IReadOnlyCollection<string> draftIds, CancellationToken cancellationToken)
    {
        ValidateDraftIds(draftIds);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var drafts = await Records.ReadDraftsAsync(db, [.. draftIds], cancellationToken);
        var result = await ReadAggregatesAsync(db, drafts, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<DocumentChangeReviewPage> ListAsync(PageCursor? cursor, int limit, DraftStatusFilter filter,
        CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var slice = await Records.PageAsync(db, cursor, limit, filter, cancellationToken);
        var histories = await Records.ReadHistoriesAsync(db, slice.Items, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new([.. slice.Items.Select(row => ToListItem(row, histories[row.DraftId]))], slice.NewerCursor, slice.OlderCursor);
    }

    public async Task<NewDocumentChangePage> ListEligibleComparisonsAsync(PageCursor? cursor, int limit,
        CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = from comparison in ReviewQueries.EligibleComparisons(db)
                   join extraction in db.Set<DocumentExtractionRow>() on comparison.AfterExtractionId equals extraction.ExtractionId
                   join attempt in db.Attempts on extraction.AttemptId equals attempt.AttemptId
                   select new EligibleRow
                   {
                       ComparisonId = comparison.ComparisonId,
                       ObservedTicks = attempt.ObservedAtUtcTicks,
                       SourceId = attempt.SourceId,
                       RequestedUrl = attempt.RequestedUrl,
                       ResultJson = comparison.ResultJson
                   };
        var fetched = await ReviewKeyset.Page(rows.AsNoTracking(), cursor, row => row.ObservedTicks, row => row.ComparisonId).Take(limit + 1).ToArrayAsync(cancellationToken);
        var slice = KeysetSlice<EligibleRow>.Create(fetched, limit, cursor, row => (row.ObservedTicks, row.ComparisonId));
        return new([.. slice.Items.Select(ToNewChange)], slice.NewerCursor, slice.OlderCursor);
    }

    private static NewDocumentChange ToNewChange(EligibleRow row)
    {
        using var result = JsonDocument.Parse(row.ResultJson);
        var hunks = result.RootElement.GetProperty("hunks");
        if (hunks.GetArrayLength() == 0)
            throw new InvalidOperationException("A stored comparison marked as changed has no passages.");
        var firstPassage = hunks[0].Deserialize<DocumentComparisonHunk>(CollectionProtocol.JsonOptions)
            ?? throw new InvalidOperationException("Stored comparison passage is missing.");
        return new(row.ComparisonId, row.SourceId, row.RequestedUrl, new DateTimeOffset(row.ObservedTicks, TimeSpan.Zero),
            hunks.GetArrayLength(), firstPassage);
    }

    private sealed record EligibleRow
    {
        public string ComparisonId { get; init; } = "";
        public long ObservedTicks { get; init; }
        public string SourceId { get; init; } = "";
        public string RequestedUrl { get; init; } = "";
        public string ResultJson { get; init; } = "";
    }

    public async Task<IReadOnlyList<UnpublishedApprovedDraft>> ListUnpublishedApprovedDraftsAsync(int limit,
        CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var records = await Records.ReadUnpublishedApprovedAsync(db, ReviewQueries.UnpublishedApprovedDrafts, limit, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return [.. records.Select(record => new UnpublishedApprovedDraft(ToListItem(record.Draft, record.History),
            record.PublishedRevisionNumber))];
    }

    public async Task<ReviewOverview> GetOverviewAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var newChanges = await ReviewQueries.EligibleComparisons(db).CountAsync(cancellationToken);
        var needingAction = await Records.Drafts(db, DraftStatusFilter.NeedsAction).CountAsync(cancellationToken);
        var approved = await Records.Drafts(db, DraftStatusFilter.Approved).CountAsync(cancellationToken);
        return new(newChanges, needingAction, approved);
    }

    public async Task<IReadOnlyList<ReviewActivityEvent>> ListRecentActivityAsync(int limit, CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var drafts = await db.Set<DocumentChangeDraftRow>().AsNoTracking().OrderByDescending(row => row.CreatedAtUtcTicks)
            .ThenByDescending(row => row.DraftId).Take(limit).ToArrayAsync(cancellationToken);
        var saved = await db.Set<DocumentChangeRevisionRow>().AsNoTracking().Where(row => row.RevisionNumber > 1)
            .OrderByDescending(row => row.CreatedAtUtcTicks).ThenByDescending(row => row.DraftId)
            .ThenByDescending(row => row.RevisionNumber).Take(limit).ToArrayAsync(cancellationToken);
        var decided = await db.Set<ReviewDecisionRow>().AsNoTracking().OrderByDescending(row => row.CreatedAtUtcTicks)
            .ThenByDescending(row => row.DecisionId).Take(limit).ToArrayAsync(cancellationToken);
        var revisions = await ReadActivityRevisionsAsync(db, drafts, decided, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return
        [
            .. drafts.Select(row => ToActivity(ReviewActivityKind.DraftCreated, row.CreatedAtUtcTicks, revisions[(row.DraftId, 1)])),
            .. saved.Select(row => ToActivity(ReviewActivityKind.RevisionSaved, row.CreatedAtUtcTicks,
                ReviewJson.Deserialize<DocumentChangeDraftRevision>(row.RevisionJson, "revision"))),
            .. decided.Select(row => ToDecisionActivity(row, revisions[(row.DraftId, row.RevisionNumber)]))
        ];
    }

    private static async Task<Dictionary<(string DraftId, int RevisionNumber), DocumentChangeDraftRevision>> ReadActivityRevisionsAsync(
        CollectionAttemptDbContext db, DocumentChangeDraftRow[] drafts, ReviewDecisionRow[] decisions,
        CancellationToken cancellationToken)
    {
        var wanted = drafts.Select(row => (row.DraftId, RevisionNumber: 1))
            .Concat(decisions.Select(row => (row.DraftId, row.RevisionNumber))).ToHashSet();
        var draftIds = wanted.Select(key => key.DraftId).Distinct().ToArray();
        var numbers = wanted.Select(key => key.RevisionNumber).Distinct().ToArray();
        var rows = await db.Set<DocumentChangeRevisionRow>().AsNoTracking()
            .Where(row => draftIds.Contains(row.DraftId) && numbers.Contains(row.RevisionNumber))
            .ToArrayAsync(cancellationToken);
        return rows.Where(row => wanted.Contains((row.DraftId, row.RevisionNumber))).ToDictionary(
            row => (row.DraftId, row.RevisionNumber), row => ReviewJson.Deserialize<DocumentChangeDraftRevision>(row.RevisionJson, "revision"));
    }

    private static ReviewActivityEvent ToActivity(ReviewActivityKind kind, long ticks, DocumentChangeDraftRevision revision) =>
        new(kind, new DateTimeOffset(ticks, TimeSpan.Zero), revision.AuthorSubject, revision.DraftId, revision.Headline, null);

    private static ReviewActivityEvent ToDecisionActivity(ReviewDecisionRow row, DocumentChangeDraftRevision revision) =>
        new(ReviewActivityKind.DecisionRecorded, new DateTimeOffset(row.CreatedAtUtcTicks, TimeSpan.Zero),
            ReviewJson.Deserialize<ReviewDecision>(row.DecisionJson, "decision").ActorSubject, row.DraftId, revision.Headline, row.Kind);

    internal sealed record VerifiedComparison(EligibleDocumentComparison Summary,
        DocumentExtraction Before, DocumentExtraction After);

    internal static async Task<VerifiedComparison?> ReadVerifiedComparisonAsync(CollectionAttemptDbContext db,
        string comparisonId, CancellationToken cancellationToken) =>
        (await ReadVerifiedComparisonsAsync(db, [comparisonId], cancellationToken)).GetValueOrDefault(comparisonId);

    private static async Task<Dictionary<string, VerifiedComparison>> ReadVerifiedComparisonsAsync(
        CollectionAttemptDbContext db, string[] comparisonIds, CancellationToken cancellationToken)
    {
        var rows = await db.Set<DocumentComparisonRow>().AsNoTracking()
            .Where(row => comparisonIds.Contains(row.ComparisonId)).ToArrayAsync(cancellationToken);
        var extractionIds = rows.SelectMany(row => new[] { row.BeforeExtractionId, row.AfterExtractionId })
            .Distinct(StringComparer.Ordinal).ToArray();
        var extractions = await PostgresDocumentExtractionStore.ReadManyAsync(db, extractionIds, cancellationToken);
        var verified = new Dictionary<string, VerifiedComparison>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var comparison = VerifyComparison(row, extractions, cancellationToken);
            if (comparison is not null) verified.Add(row.ComparisonId, comparison);
        }
        return verified;
    }

    private static VerifiedComparison? VerifyComparison(DocumentComparisonRow row,
        Dictionary<string, DocumentExtraction> extractions, CancellationToken cancellationToken)
    {
        var before = extractions.GetValueOrDefault(row.BeforeExtractionId)
            ?? throw new InvalidOperationException("Comparison's before extraction is missing.");
        var after = extractions.GetValueOrDefault(row.AfterExtractionId)
            ?? throw new InvalidOperationException("Comparison's after extraction is missing.");
        var comparison = DocumentComparison.Create(before, after, cancellationToken);
        if (row.ComparisonId != comparison.ComparisonId || row.AlgorithmVersion != comparison.AlgorithmVersion ||
            row.SettingsVersion != comparison.SettingsVersion || row.ResultJson != JsonSerializer.Serialize(comparison, CollectionProtocol.JsonOptions))
            throw new InvalidOperationException("Stored comparison does not match its retained extraction evidence.");
        if (!comparison.HasChanges()) return null;
        if (before.SourceAttempt.SourceId != after.SourceAttempt.SourceId ||
            before.SourceAttempt.RequestedUrl != after.SourceAttempt.RequestedUrl)
            throw new InvalidOperationException("Stored comparison sources are incompatible.");
        return new(new(before.SourceAttempt.SourceId, before.SourceAttempt.RequestedUrl,
            before.SourceAttempt.FinalUrl, after.SourceAttempt.FinalUrl,
            before.SourceAttempt.ObservedAt, after.SourceAttempt.ObservedAt, comparison), before, after);
    }

    internal static Task<string?> ReadDraftIdForComparisonAsync(CollectionAttemptDbContext db, string comparisonId,
        CancellationToken cancellationToken) => db.Set<DocumentChangeDraftRow>().AsNoTracking()
            .Where(row => row.ComparisonId == comparisonId).OrderBy(row => row.CreatedAtUtcTicks).ThenBy(row => row.DraftId)
            .Select(row => row.DraftId).FirstOrDefaultAsync(cancellationToken);

    internal static async Task<bool> TryAddAnalysisDraftAsync(CollectionAttemptDbContext db,
        DocumentChangeDraftRevision revision, CancellationToken cancellationToken)
    {
        if (revision.RevisionNumber != 1 || !ReviewAuthor.IsAnalysis(revision.AuthorSubject))
            throw new InvalidOperationException("An analysis draft must be revision 1 by the analysis author.");
        await PostgresAdvisoryLock.AcquireAsync(db, PostgresAdvisoryLock.DocumentChangeReview, cancellationToken);
        if (await ReadDraftIdForComparisonAsync(db, revision.ComparisonId, cancellationToken) is not null) return false;
        await ValidateRevisionAgainstEvidenceAsync(db, revision, cancellationToken);
        AddDraft(db, revision);
        return true;
    }

    private static void AddDraft(CollectionAttemptDbContext db, DocumentChangeDraftRevision revision)
    {
        db.Add(new DocumentChangeDraftRow
        {
            DraftId = revision.DraftId,
            ComparisonId = revision.ComparisonId,
            CurrentRevisionNumber = 1,
            ReviewStateVersion = 0,
            ReviewStatus = ReviewPolicy.GetInitialStatus(1),
            CreatedAtUtcTicks = revision.CreatedAtUtc.UtcTicks
        });
        db.Add(NewRevisionRow(revision));
    }

    private static ReviewDecisionRow NewDecisionRow(ReviewDecision decision) => new()
    {
        DecisionId = decision.DecisionId,
        DraftId = decision.DraftId,
        RevisionNumber = decision.RevisionNumber,
        ReviewStateVersion = decision.ReviewStateVersion,
        DecisionJson = ReviewJson.Serialize(decision),
        Kind = decision.Kind,
        CreatedAtUtcTicks = decision.CreatedAtUtc.UtcTicks
    };

    private static DocumentChangeRevisionRow NewRevisionRow(DocumentChangeDraftRevision revision) => new()
    {
        DraftId = revision.DraftId,
        RevisionNumber = revision.RevisionNumber,
        RevisionJson = ReviewJson.Serialize(revision),
        CreatedAtUtcTicks = revision.CreatedAtUtc.UtcTicks
    };

    private static ImmutableArray<SourceSpan> CreateInitialCitations(DocumentComparison comparison)
    {
        var citations = ImmutableArray.CreateBuilder<SourceSpan>();
        foreach (var hunk in comparison.Hunks.Take(32))
        {
            if (hunk.BeforeLength > 0)
                citations.Add(new(comparison.BeforeExtractionId, hunk.BeforeStart, hunk.BeforeLength));
            if (hunk.AfterLength > 0)
                citations.Add(new(comparison.AfterExtractionId, hunk.AfterStart, hunk.AfterLength));
        }
        return citations.ToImmutable();
    }

    private static async Task ValidateRevisionAgainstEvidenceAsync(CollectionAttemptDbContext db,
        DocumentChangeDraftRevision revision, CancellationToken cancellationToken)
    {
        var verified = await ReadVerifiedComparisonAsync(db, revision.ComparisonId, cancellationToken)
            ?? throw new ArgumentException("Drafts must reference a complete comparison containing a text change.", nameof(revision));
        ValidateRevisionAgainstEvidence(revision, verified);
    }

    private static void ValidateRevisionAgainstEvidence(DocumentChangeDraftRevision revision, VerifiedComparison verified)
    {
        revision.ValidateShape();
        if (revision.ComparisonId != verified.Summary.Comparison.ComparisonId)
            throw new InvalidOperationException("Revision comparison binding is inconsistent.");
        foreach (var citation in revision.Citations)
        {
            var extraction = citation.ExtractionId == verified.Before.ExtractionId ? verified.Before
                : citation.ExtractionId == verified.After.ExtractionId ? verified.After
                : throw new ArgumentException("Citations must bind to this comparison's before or after extraction.", nameof(revision));
            _ = new DocumentTextSpan(extraction, citation.Start, citation.Length);
        }
    }

    private static async Task<Dictionary<string, PublishableDocumentChange>> ReadAggregatesAsync(
        CollectionAttemptDbContext db, DocumentChangeDraftRow[] drafts, CancellationToken cancellationToken)
    {
        if (drafts.Length == 0) return [];
        var histories = await Records.ReadHistoriesAsync(db, drafts, cancellationToken);
        var comparisonIds = drafts.Select(draft => draft.ComparisonId).Distinct(StringComparer.Ordinal).ToArray();
        var comparisons = await ReadVerifiedComparisonsAsync(db, comparisonIds, cancellationToken);
        return drafts.ToDictionary(draft => draft.DraftId, draft => BuildAggregate(draft, histories[draft.DraftId], comparisons),
            StringComparer.Ordinal);
    }

    private static PublishableDocumentChange BuildAggregate(DocumentChangeDraftRow draft,
        ReviewHistory<DocumentChangeDraftRevision> history, Dictionary<string, VerifiedComparison> comparisons)
    {
        var verified = ValidateAgainstEvidence(draft, history, comparisons);
        var review = new DocumentChangeReview(draft.DraftId, history.CurrentRevision!, draft.ReviewStateVersion, history.LoadedRevisions,
            history.Decisions, history.UnresolvedConcerns);
        return new(review, verified.Summary, verified.Before, verified.After);
    }

    private static VerifiedComparison ValidateAgainstEvidence(DocumentChangeDraftRow draft,
        ReviewHistory<DocumentChangeDraftRevision> history, Dictionary<string, VerifiedComparison> comparisons)
    {
        RequireBoundComparison(draft, history);
        var verified = comparisons.GetValueOrDefault(draft.ComparisonId)
            ?? throw new InvalidOperationException("Retained draft comparison is no longer valid.");
        foreach (var revision in history.LoadedRevisions) ValidateRevisionAgainstEvidence(revision, verified);
        return verified;
    }

    internal static DocumentChangeReviewListItem ToListItem(DocumentChangeDraftRow draft,
        ReviewHistory<DocumentChangeDraftRevision> history)
    {
        RequireBoundComparison(draft, history);
        return new(draft.DraftId, history.CurrentRevision!, draft.ReviewStateVersion, history.CurrentStatus,
            history.UnresolvedConcerns.Length);
    }

    private static void RequireBoundComparison(DocumentChangeDraftRow draft, ReviewHistory<DocumentChangeDraftRevision> history)
    {
        if (history.LoadedRevisions.Any(revision => revision.ComparisonId != draft.ComparisonId))
            throw new InvalidOperationException("A saved revision changed its bound comparison.");
    }

    private static void ValidateDraftIds(IReadOnlyCollection<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count is < 1 or > PublishRelease.MaximumRecords) throw new ArgumentOutOfRangeException(nameof(values));
        foreach (var value in values) DocumentChangeDraftRevision.ValidateDraftId(value);
    }
}
