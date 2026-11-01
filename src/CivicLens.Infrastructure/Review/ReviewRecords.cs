using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Review;

/// <summary>The shared store core. Reads hold one review cycle: the current revision, the last approved revision, and the
/// decisions since that approval.</summary>
internal sealed class ReviewRecords<TDraftRow, TRevisionRow, TDecisionRow, TRevision>(string lockKey, string draftName,
    string recordIdProperty, Action<TDraftRow, TRevision>? revised = null)
    where TDraftRow : class, IReviewDraftRow
    where TRevisionRow : class, IReviewRevisionRow
    where TDecisionRow : class, IReviewDecisionRow, new()
    where TRevision : class, IReviewRevision
{
    public async Task<TRevision> CreateAsync(IDbContextFactory<CollectionAttemptDbContext> contextFactory, string actorSubject,
        TRevision revision, string operation, string idempotencyKey, string payloadHash,
        Func<CollectionAttemptDbContext, Task<TRevision>> create, CancellationToken cancellationToken)
    {
        ReviewIdempotency.ValidateRequest(actorSubject, idempotencyKey, payloadHash);
        ArgumentNullException.ThrowIfNull(revision);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await ReviewIdempotency.WriteOnceAsync(db, lockKey, actorSubject, operation, idempotencyKey, payloadHash, async () =>
        {
            if (revision.RevisionNumber != 1) throw new InvalidOperationException("A draft must begin at revision one.");
            ReviewWriteRules.RequireRevisionAuthor(revision.AuthorSubject, actorSubject);
            return await create(db);
        }, cancellationToken);
    }

    private static readonly string[] IdentityProperties = ["revisionNumber", "authorSubject", "authorKind", "createdAtUtc", "textOrigin"];

    /// <summary>The approval that a save carries. It runs in the save transaction, on the revision that the save wrote.
    /// <paramref name="MustWriteRevision"/> tells that an unchanged input still needs a new person revision.</summary>
    public sealed record ApprovalAfterSave(ReviewDecision Decision,
        Func<CollectionAttemptDbContext, TDraftRow, ReviewHistory<TRevision>, Task> Validate,
        Func<CollectionAttemptDbContext, string, int, CancellationToken, Task> AfterDecision,
        Func<TRevision, TRevision> Comparable, Func<TRevision, bool>? MustWriteRevision = null);

    /// <summary>Saves a revision. With an approval, the same transaction then approves the saved revision with every check of a
    /// separate decision. A failed check rolls the saved revision back. When the input equals the current revision, no revision is
    /// written and the approval binds to the current one. That refuses when the current revision is already approved.</summary>
    public async Task<TRevision> SaveRevisionAsync(IDbContextFactory<CollectionAttemptDbContext> contextFactory, string actorSubject,
        TRevision revision, int expectedRevisionNumber, string operation, string idempotencyKey, string payloadHash,
        Func<CollectionAttemptDbContext, TDraftRow, TRevision, Task<TRevision>> prepareAndList, Func<TRevision, TRevisionRow> toRow,
        ApprovalAfterSave? approval, CancellationToken cancellationToken)
    {
        ReviewIdempotency.ValidateRequest(actorSubject, idempotencyKey, payloadHash);
        ArgumentNullException.ThrowIfNull(revision);
        if (approval is not null) ReviewWriteRules.RequireDecisionActor(approval.Decision, actorSubject);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await ReviewIdempotency.WriteOnceAsync(db, lockKey, actorSubject, operation, idempotencyKey, payloadHash, async () =>
        {
            var draft = await ReadDraftForWriteAsync(db, revision.RecordId, cancellationToken);
            var cycle = await ReadCycleAsync(db, draft, cancellationToken);
            ReviewWriteRules.RequireSaveable(draft.CurrentRevisionNumber, expectedRevisionNumber, revision.RevisionNumber);
            ReviewWriteRules.RequireRevisionAuthor(revision.AuthorSubject, actorSubject);
            var current = await FindRevisionAsync(db, draft.RecordId, draft.CurrentRevisionNumber, cancellationToken)
                ?? throw new InvalidOperationException("Current draft revision is missing.");
            var saved = await prepareAndList(db, draft, current);
            if (approval is not null && !(approval.MustWriteRevision?.Invoke(current) ?? false) && HasSameContent(current, saved, approval.Comparable))
            {
                if (draft.ReviewStatus == ReviewStatus.Approved) throw new NothingToSaveException();
                await ApproveSavedAsync(db, approval with { Decision = approval.Decision with { RevisionNumber = current.RevisionNumber } },
                    cancellationToken);
                return current;
            }
            AppendRevision(db, draft, saved, toRow(saved), cycle);
            if (approval is null) return saved;
            await db.SaveChangesAsync(cancellationToken);
            await ApproveSavedAsync(db, approval, cancellationToken);
            return saved;
        }, cancellationToken);
    }

    private static bool HasSameContent(TRevision current, TRevision saved, Func<TRevision, TRevision> comparable) =>
        JsonNode.DeepEquals(WithoutIdentity(comparable(current)), WithoutIdentity(comparable(saved)));

    private static JsonObject WithoutIdentity(TRevision revision)
    {
        var node = ReviewJson.ToObject(revision);
        foreach (var property in IdentityProperties) node.Remove(property);
        return node;
    }

    private Task<ReviewDecision> ApproveSavedAsync(CollectionAttemptDbContext db, ApprovalAfterSave approval,
        CancellationToken cancellationToken) =>
        DecideInTransactionAsync(db, approval.Decision, approval.Decision.RevisionNumber, approval.Decision.ReviewStateVersion - 1,
            approval.Validate, approval.AfterDecision, cancellationToken);

    /// <summary>The one step that appends a revision for a person or for analysis. It recomputes the stored status. It clears the
    /// held reason, because the reason described an older revision.</summary>
    public void AppendRevision(CollectionAttemptDbContext db, TDraftRow draft, TRevision revision, TRevisionRow row,
        ImmutableArray<ReviewDecision> cycle)
    {
        db.Add(row);
        draft.CurrentRevisionNumber = revision.RevisionNumber;
        draft.LastRevisedAtUtcTicks = row.CreatedAtUtcTicks;
        SetHeldReason(draft, null);
        revised?.Invoke(draft, revision);
        draft.ReviewStatus = ReviewPolicy.GetCurrentStatus(draft.CurrentRevisionNumber, cycle);
    }

    /// <summary>The analysis worker appends a revision of a fact record. Code approves it in the same transaction when no concern
    /// is open, the revision holds no text that a person wrote, and the record's own approval checks pass. Otherwise it waits for a person.
    /// A failed approval check keeps its message as the held reason of the draft. The caller runs
    /// <see cref="AfterCodeApprovalAsync"/> once the rest of the transaction rows are tracked.</summary>
    public async Task AppendCodeApprovedAsync(CollectionAttemptDbContext db, TDraftRow draft, TRevision revision, TRevisionRow row,
        ImmutableArray<ReviewDecision> cycle, Func<Task<ApprovalCheck>> approvalChecks)
    {
        AppendRevision(db, draft, revision, row, cycle);
        if (!ReviewPolicy.GetUnresolvedConcerns(cycle).IsEmpty || revision is IHeadlineSummaryRevision { TextOrigin: ReviewTextOrigin.Person })
            return;
        var check = await approvalChecks();
        if (!check.Passed)
        {
            SetHeldReason(draft, check.Failure);
            return;
        }
        var decision = new ReviewDecision(Guid.NewGuid().ToString("N"), draft.RecordId, revision.RevisionNumber, draft.ReviewStateVersion + 1,
            ReviewDecisionKind.Approve, ReviewAuthor.CodeSubject, null, [], new DateTimeOffset(row.CreatedAtUtcTicks, TimeSpan.Zero));
        ReviewPolicy.ValidateDecision(draft.RecordId, draft.CurrentRevisionNumber, draft.ReviewStateVersion, cycle, decision);
        RecordDecision(db, draft, cycle, decision);
    }

    /// <summary>Runs the after-decision hooks for a code approval that this transaction tracked. The hooks save the context, so the
    /// caller must call this after it adds the rows that the revision row refers to.</summary>
    public async Task AfterCodeApprovalAsync(CollectionAttemptDbContext db, string recordId, int revisionNumber,
        Func<CollectionAttemptDbContext, string, int, CancellationToken, Task> afterDecision, CancellationToken cancellationToken)
    {
        var approved = db.Set<TDraftRow>().Local.Any(draft => draft.RecordId == recordId && draft.CurrentRevisionNumber == revisionNumber &&
            draft.ReviewStatus == ReviewStatus.Approved);
        if (approved) await afterDecision(db, recordId, revisionNumber, cancellationToken);
    }

    private void RecordDecision(CollectionAttemptDbContext db, TDraftRow draft, ImmutableArray<ReviewDecision> cycle, ReviewDecision decision)
    {
        db.Add(NewDecisionRow(decision));
        db.AddRange(decision.ResolvedDecisionIds.Select(resolvedId => new ReviewDecisionResolutionRow<TDecisionRow>
        {
            DecisionId = decision.DecisionId,
            RecordId = decision.DraftId,
            ResolvedDecisionId = resolvedId
        }));
        draft.ReviewStateVersion = decision.ReviewStateVersion;
        SetHeldReason(draft, null);
        draft.ReviewStatus = ReviewPolicy.GetCurrentStatus(draft.CurrentRevisionNumber, cycle.Add(decision));
    }

    /// <summary>The one writer of decision columns. The columns and the JSON come from the same decision, so they cannot differ.</summary>
    private static TDecisionRow NewDecisionRow(ReviewDecision decision) => new()
    {
        DecisionId = decision.DecisionId,
        RecordId = decision.DraftId,
        RevisionNumber = decision.RevisionNumber,
        ReviewStateVersion = decision.ReviewStateVersion,
        DecisionJson = ReviewJson.Serialize(decision),
        Kind = decision.Kind,
        ActorSubject = decision.ActorSubject,
        Note = decision.Note,
        CorrectionNote = decision.CorrectionNote,
        CreatedAtUtcTicks = decision.CreatedAtUtc.UtcTicks
    };

    private static void SetHeldReason(TDraftRow draft, string? reason)
    {
        if (draft is IHeldDraftRow held) held.HeldReason = reason;
    }

    public async Task<ReviewDecision> DecideAsync(IDbContextFactory<CollectionAttemptDbContext> contextFactory, string actorSubject,
        ReviewDecision decision, int expectedRevisionNumber, int expectedReviewStateVersion, string operation, string idempotencyKey,
        string payloadHash, Func<CollectionAttemptDbContext, TDraftRow, ReviewHistory<TRevision>, Task> validateDecision,
        Func<CollectionAttemptDbContext, string, int, CancellationToken, Task> afterDecision,
        CancellationToken cancellationToken)
    {
        ReviewIdempotency.ValidateRequest(actorSubject, idempotencyKey, payloadHash);
        ReviewWriteRules.RequireDecisionActor(decision, actorSubject);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await ReviewIdempotency.WriteOnceAsync(db, lockKey, actorSubject, operation, idempotencyKey, payloadHash,
            () => DecideInTransactionAsync(db, decision, expectedRevisionNumber, expectedReviewStateVersion, validateDecision,
                afterDecision, cancellationToken), cancellationToken);
    }

    private async Task<ReviewDecision> DecideInTransactionAsync(CollectionAttemptDbContext db, ReviewDecision decision,
        int expectedRevisionNumber, int expectedReviewStateVersion,
        Func<CollectionAttemptDbContext, TDraftRow, ReviewHistory<TRevision>, Task> validateDecision,
        Func<CollectionAttemptDbContext, string, int, CancellationToken, Task> afterDecision, CancellationToken cancellationToken)
    {
        var draft = await ReadDraftForWriteAsync(db, decision.DraftId, cancellationToken);
        var history = (await ReadHistoriesAsync(db, [draft], cancellationToken))[draft.RecordId];
        ReviewWriteRules.RequireDecidable(draft.CurrentRevisionNumber, draft.ReviewStateVersion, expectedRevisionNumber,
            expectedReviewStateVersion, decision);
        ReviewPolicy.ValidateDecision(draft.RecordId, draft.CurrentRevisionNumber, draft.ReviewStateVersion, history.Decisions, decision);
        await validateDecision(db, draft, history);
        RecordDecision(db, draft, history.Decisions, decision);
        await afterDecision(db, draft.RecordId, decision.RevisionNumber, cancellationToken);
        return decision;
    }

    public IQueryable<TDraftRow> Drafts(CollectionAttemptDbContext db, DraftStatusFilter filter)
    {
        var drafts = db.Set<TDraftRow>().AsNoTracking();
        return filter switch
        {
            DraftStatusFilter.All => drafts,
            DraftStatusFilter.NeedsAction => drafts.Where(draft => draft.ReviewStatus != ReviewStatus.Approved),
            DraftStatusFilter.Approved => drafts.Where(draft => draft.ReviewStatus == ReviewStatus.Approved),
            _ => throw new ArgumentOutOfRangeException(nameof(filter))
        };
    }

    public async Task<(int NeedsAction, int Approved)> CountAsync(CollectionAttemptDbContext db, CancellationToken cancellationToken) =>
        (await Drafts(db, DraftStatusFilter.NeedsAction).CountAsync(cancellationToken),
            await Drafts(db, DraftStatusFilter.Approved).CountAsync(cancellationToken));

    public Task<TDraftRow[]> ReadDraftsAsync(CollectionAttemptDbContext db, string[] recordIds, CancellationToken cancellationToken) =>
        db.Set<TDraftRow>().AsNoTracking().Where(row => recordIds.Contains(EF.Property<string>(row, recordIdProperty)))
            .ToArrayAsync(cancellationToken);

    /// <summary>Reads each draft for the queue. A draft whose stored data cannot be read keeps its parse error, so one bad record does
    /// not hide the others.</summary>
    public async Task<(Dictionary<string, QueueRead<TRevision>> Readable, Dictionary<string, Exception> Unreadable)> ReadQueueAsync(
        CollectionAttemptDbContext db, string[] recordIds, CancellationToken cancellationToken)
    {
        var drafts = await ReadDraftsAsync(db, recordIds, cancellationToken);
        var (histories, errors) = await ReadHistoriesOrUnreadableAsync(db, drafts, cancellationToken);
        var readable = drafts.Where(draft => histories.ContainsKey(draft.RecordId)).ToDictionary(draft => draft.RecordId, draft =>
        {
            var history = histories[draft.RecordId];
            return new QueueRead<TRevision>(history.CurrentRevision!, history.CurrentStatus, history.UnresolvedConcerns.Length,
                (draft as IHeldDraftRow)?.HeldReason);
        }, StringComparer.Ordinal);
        return (readable, errors);
    }

    public async Task<Dictionary<string, QueueDraft>> ReadQueueDraftsAsync(CollectionAttemptDbContext db, string[] recordIds,
        Func<TRevision, string> headlineOf, CancellationToken cancellationToken)
    {
        var (readable, unreadable) = await ReadQueueAsync(db, recordIds, cancellationToken);
        var drafts = readable.ToDictionary(read => read.Key, read => read.Value.ToDraft(headlineOf(read.Value.Revision)),
            StringComparer.Ordinal);
        foreach (var (recordId, cause) in unreadable) drafts[recordId] = QueueDraft.Unreadable(recordId, cause);
        return drafts;
    }

    public async Task<IReadOnlyList<(TDraftRow Draft, ReviewHistory<TRevision> History)>>
        ReadUnpublishedApprovedAsync(CollectionAttemptDbContext db, string unpublishedApprovedSql, int limit,
            IReadOnlyCollection<string> excludedRecordIds, CancellationToken cancellationToken)
    {
        string[] excluded = [.. excludedRecordIds];
        var keys = await db.Database.SqlQueryRaw<UnpublishedKey>(unpublishedApprovedSql)
            .Where(key => key.PublishedRevisionNumber == null || key.PublishedRevisionNumber != key.CurrentRevisionNumber)
            .Where(key => !excluded.Contains(key.RecordId))
            .OrderByDescending(key => key.CreatedAtUtcTicks).ThenByDescending(key => key.RecordId)
            .Take(limit).ToArrayAsync(cancellationToken);
        var drafts = (await ReadDraftsAsync(db, [.. keys.Select(key => key.RecordId)], cancellationToken))
            .ToDictionary(row => row.RecordId, StringComparer.Ordinal);
        var histories = await ReadHistoriesAsync(db, drafts.Values, cancellationToken);
        return [.. keys.Select(key => (drafts[key.RecordId], histories[key.RecordId]))];
    }

    public async Task<Dictionary<string, ReviewHistory<TRevision>>> ReadHistoriesAsync(CollectionAttemptDbContext db,
        IReadOnlyCollection<TDraftRow> drafts, CancellationToken cancellationToken)
    {
        var (histories, unreadable) = await ReadHistoriesOrUnreadableAsync(db, drafts, cancellationToken);
        if (unreadable.Count > 0) ExceptionDispatchInfo.Throw(unreadable.Values.First());
        return histories;
    }

    /// <summary>Reads the rows of every draft in a fixed number of queries, then parses each draft alone. Only an error from the parse or from the build
    /// of the history marks a draft unreadable (see <see cref="ReviewJson.IsUnreadableStoredData"/>). That covers a missing revision and
    /// failed shape or history checks. A failed query is not caught, so a database error fails the whole read.</summary>
    public async Task<(Dictionary<string, ReviewHistory<TRevision>> Histories, Dictionary<string, Exception> Unreadable)>
        ReadHistoriesOrUnreadableAsync(CollectionAttemptDbContext db, IReadOnlyCollection<TDraftRow> drafts, CancellationToken cancellationToken)
    {
        Dictionary<string, ReviewHistory<TRevision>> histories = [];
        Dictionary<string, Exception> unreadable = [];
        if (drafts.Count == 0) return (histories, unreadable);
        var cycleRows = await ReadCycleRowsAsync(db, [.. drafts.Where(draft => draft.ReviewStateVersion > 0).Select(draft => draft.RecordId)],
            cancellationToken);
        Dictionary<string, ImmutableArray<ReviewDecision>> cycles = new(StringComparer.Ordinal);
        foreach (var draft in drafts)
        {
            try { cycles[draft.RecordId] = cycleRows.ToCycle(draft.RecordId, draft.ReviewStateVersion); }
            catch (Exception exception) when (ReviewJson.IsUnreadableStoredData(exception)) { unreadable[draft.RecordId] = exception; }
        }
        var readable = drafts.Where(draft => cycles.ContainsKey(draft.RecordId)).ToArray();
        var revisionRows = await ReadCycleRevisionRowsAsync(db, [.. readable.Where(draft => draft.CurrentRevisionNumber > 0).Select(draft => draft.RecordId)],
            [.. cycles.Values.Where(cycle => ReviewDecisionHistory.ApprovalVersionOf(cycle) > 0).Select(cycle => cycle[0].DecisionId)],
            cancellationToken);
        foreach (var draft in readable)
        {
            try { histories[draft.RecordId] = BuildHistory(draft, cycles[draft.RecordId], revisionRows); }
            catch (Exception exception) when (ReviewJson.IsUnreadableStoredData(exception)) { unreadable[draft.RecordId] = exception; }
        }
        return (histories, unreadable);
    }

    public async Task<ImmutableArray<ReviewDecision>> ReadCycleAsync(CollectionAttemptDbContext db, TDraftRow draft,
        CancellationToken cancellationToken)
    {
        if (draft.ReviewStateVersion == 0) return [];
        var rows = await ReadCycleRowsAsync(db, [draft.RecordId], cancellationToken);
        return rows.ToCycle(draft.RecordId, draft.ReviewStateVersion);
    }

    /// <summary>The decision rows of the current cycles and the concerns that their approvals resolve. A draft builds its own decisions from
    /// them, so one bad row makes only its own draft unreadable.</summary>
    private sealed record CycleRows(ILookup<string, TDecisionRow> Decisions, ILookup<string, string> Resolved)
    {
        public ImmutableArray<ReviewDecision> ToCycle(string recordId, int reviewStateVersion) =>
            ReviewDecisionHistory.RequireContiguousCycle(reviewStateVersion,
                [.. Decisions[recordId].Select(row => ReviewDecisionHistory.ToDecision(row, [.. Resolved[row.DecisionId]]))]);
    }

    private async Task<CycleRows> ReadCycleRowsAsync(CollectionAttemptDbContext db, string[] recordIds, CancellationToken cancellationToken)
    {
        if (recordIds.Length == 0) return new(Array.Empty<TDecisionRow>().ToLookup(row => row.RecordId), Array.Empty<string>().ToLookup(id => id));
        var rows = await db.Set<TDecisionRow>().AsNoTracking()
            .Where(row => recordIds.Contains(EF.Property<string>(row, recordIdProperty)) &&
                row.ReviewStateVersion >= (db.Set<TDecisionRow>()
                    .Where(approval => EF.Property<string>(approval, recordIdProperty) == EF.Property<string>(row, recordIdProperty) &&
                        approval.Kind == ReviewDecisionKind.Approve)
                    .Max(approval => (int?)approval.ReviewStateVersion) ?? 0))
            .OrderBy(row => EF.Property<string>(row, recordIdProperty)).ThenBy(row => row.ReviewStateVersion)
            .Take(recordIds.Length * ReviewPolicy.MaximumDecisionsPerCycle).ToArrayAsync(cancellationToken);
        return new(rows.ToLookup(row => row.RecordId, StringComparer.Ordinal),
            await ReadResolvedConcernsAsync(db, [.. rows.Where(row => row.Kind == ReviewDecisionKind.Approve).Select(row => row.DecisionId)],
                cancellationToken));
    }

    private static async Task<ILookup<string, string>> ReadResolvedConcernsAsync(CollectionAttemptDbContext db, string[] approvalIds,
        CancellationToken cancellationToken)
    {
        if (approvalIds.Length == 0) return Array.Empty<string>().ToLookup(id => id);
        var resolutions = await db.Set<ReviewDecisionResolutionRow<TDecisionRow>>().AsNoTracking()
            .Where(resolution => approvalIds.Contains(resolution.DecisionId))
            .OrderBy(resolution => resolution.DecisionId).ThenBy(resolution => resolution.ResolvedDecisionId)
            .Take(approvalIds.Length * ReviewPolicy.MaximumOpenConcerns).ToArrayAsync(cancellationToken);
        return resolutions.ToLookup(resolution => resolution.DecisionId, resolution => resolution.ResolvedDecisionId, StringComparer.Ordinal);
    }

    private async Task<Dictionary<(string RecordId, int RevisionNumber), TRevisionRow>> ReadCycleRevisionRowsAsync(
        CollectionAttemptDbContext db, string[] recordIds, string[] approvalIds, CancellationToken cancellationToken)
    {
        if (recordIds.Length == 0) return [];
        var rows = await db.Set<TRevisionRow>().AsNoTracking()
            .Where(row => recordIds.Contains(EF.Property<string>(row, recordIdProperty)) &&
                (db.Set<TDraftRow>().Any(draft => EF.Property<string>(draft, recordIdProperty) == EF.Property<string>(row, recordIdProperty) &&
                        draft.CurrentRevisionNumber == row.RevisionNumber) ||
                    db.Set<TDecisionRow>().Any(approval => approvalIds.Contains(approval.DecisionId) &&
                        EF.Property<string>(approval, recordIdProperty) == EF.Property<string>(row, recordIdProperty) &&
                        approval.RevisionNumber == row.RevisionNumber)))
            .Take(recordIds.Length * 2).ToArrayAsync(cancellationToken);
        return rows.ToDictionary(row => (row.RecordId, row.RevisionNumber));
    }

    private ReviewHistory<TRevision> BuildHistory(TDraftRow draft, ImmutableArray<ReviewDecision> cycle,
        Dictionary<(string RecordId, int RevisionNumber), TRevisionRow> revisionRows)
    {
        if (draft.CurrentRevisionNumber == 0)
            return cycle.IsEmpty ? new(null, null, [], [], ReviewStatus.NotDrafted)
                : throw new InvalidOperationException("A draft without a revision has review decisions.");
        var current = ParseRevision(revisionRows.GetValueOrDefault((draft.RecordId, draft.CurrentRevisionNumber))
            ?? throw new InvalidOperationException("Current draft revision is missing."));
        var approvedNumber = ReviewDecisionHistory.ApprovedRevisionOf(cycle);
        var approved = approvedNumber == 0 ? null
            : approvedNumber == draft.CurrentRevisionNumber ? current
            : ParseRevision(revisionRows.GetValueOrDefault((draft.RecordId, approvedNumber))
                ?? throw new InvalidOperationException("Approved draft revision is missing."));
        ReviewDecisionHistory.Validate(draft.RecordId, current, approved, cycle);
        var concerns = ReviewPolicy.GetUnresolvedConcerns(cycle);
        return new(current, approved, cycle, concerns, ReviewPolicy.GetCurrentStatus(draft.CurrentRevisionNumber, cycle, concerns));
    }

    private async Task<TDraftRow> ReadDraftForWriteAsync(CollectionAttemptDbContext db, string recordId,
        CancellationToken cancellationToken) =>
        await db.Set<TDraftRow>().SingleOrDefaultAsync(row => EF.Property<string>(row, recordIdProperty) == recordId, cancellationToken)
            ?? throw new KeyNotFoundException($"{draftName} draft was not found.");

    public async Task<Dictionary<string, (TRevisionRow Row, TRevision Revision)>> ReadFirstRevisionsAsync(CollectionAttemptDbContext db,
        string[] recordIds, CancellationToken cancellationToken) =>
        (await db.Set<TRevisionRow>().AsNoTracking()
            .Where(row => recordIds.Contains(EF.Property<string>(row, recordIdProperty)) && row.RevisionNumber == 1)
            .ToArrayAsync(cancellationToken))
        .ToDictionary(row => row.RecordId, row => (row, ParseRevision(row)), StringComparer.Ordinal);

    public async Task<TRevision?> FindRevisionAsync(CollectionAttemptDbContext db, string recordId, int revisionNumber,
        CancellationToken cancellationToken) =>
        await db.Set<TRevisionRow>().AsNoTracking().SingleOrDefaultAsync(candidate =>
                EF.Property<string>(candidate, recordIdProperty) == recordId && candidate.RevisionNumber == revisionNumber, cancellationToken)
            is { } row ? ParseRevision(row) : null;

    private static TRevision ParseRevision(TRevisionRow row)
    {
        var revision = ReviewJson.Deserialize<TRevision>(row.RevisionJson, "revision");
        if (revision.RecordId != row.RecordId || revision.RevisionNumber != row.RevisionNumber)
            throw new InvalidOperationException("Stored revision identity is inconsistent.");
        revision.ValidateShape();
        return revision;
    }

    private sealed class UnpublishedKey
    {
        public string RecordId { get; set; } = "";
        public int CurrentRevisionNumber { get; set; }
        public long CreatedAtUtcTicks { get; set; }
        public int? PublishedRevisionNumber { get; set; }
    }
}
