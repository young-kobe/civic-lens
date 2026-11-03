using CivicLens.Application.Actions;
using CivicLens.Application.Review;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Actions.Persistence;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.BillStream;
using CivicLens.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Actions;

internal static class ActionDraftRows
{
    public static async Task<string?> AppendAnalysisRevisionAsync(CollectionAttemptDbContext db, ActionDraftRevision revision, string runId,
        CancellationToken cancellationToken)
    {
        revision.ValidateShape();
        await PostgresAdvisoryLock.AcquireAsync(db, PostgresAdvisoryLock.ActionReview, cancellationToken);
        var candidate = new ActionDraftCandidate(revision.CandidateId, revision.RecordId, revision.SourceId, revision.Congress, revision.BillType,
            revision.BillNumber, revision.CandidateUpdateDate, revision.CandidateIncludesSummaries, revision.CandidateIncludesSubjects);
        if (await HasNewerCandidateAsync(db, candidate, cancellationToken)) return ActionDraftErrorCodes.SupersededByNewerCandidate;
        var draft = await db.Set<ActionDraftRow>().SingleOrDefaultAsync(row => row.RecordId == revision.RecordId, cancellationToken);
        if ((draft?.CurrentRevisionNumber ?? 0) != revision.RevisionNumber - 1) return ActionDraftErrorCodes.RevisionConflict;
        if (draft?.ReviewStatus == ReviewStatus.Rejected)
        {
            if (revision.CandidateId != draft.CandidateId) draft.RefusedCandidateId = revision.CandidateId;
            return ActionDraftErrorCodes.RecordRejected;
        }
        if (draft is null) db.Add(draft = NewDraft(revision));
        var records = PostgresActionReviewStore.Records;
        await records.AppendCodeApprovedAsync(db, draft, revision, ActionRevisionRows.Create(revision, runId), await records.ReadCycleAsync(db, draft, cancellationToken),
            () => ApprovalCheck.OfAsync(() => PostgresActionReviewStore.ValidateApprovalAsync(db, revision, cancellationToken)));
        return null;
    }

    public static Task<bool> IsRejectedAsync(CollectionAttemptDbContext db, string recordId, CancellationToken cancellationToken) =>
        db.Set<ActionDraftRow>().AsNoTracking().AnyAsync(draft => draft.RecordId == recordId && draft.ReviewStatus == ReviewStatus.Rejected, cancellationToken);

    public static Task<bool> HasNewerCandidateAsync(CollectionAttemptDbContext db, ActionDraftCandidate candidate, CancellationToken cancellationToken) =>
        NewerCandidates(db, candidate).AnyAsync(cancellationToken);

    private static IQueryable<ActionCandidateRow> NewerCandidates(CollectionAttemptDbContext db, ActionDraftCandidate candidate) =>
        db.Set<ActionCandidateRow>().AsNoTracking().Where(newer => newer.SourceId == candidate.SourceId &&
                newer.Congress == candidate.Congress && newer.BillType == candidate.BillType && newer.BillNumber == candidate.BillNumber)
            .Where(ActionCandidateInputs.Supersedes(candidate.UpdateDate.UtcTicks, candidate.IncludesSummaries, candidate.IncludesSubjects));

    /// <summary>Refuses a person approval while the draft names a refused newer update, because the revision holds old facts.</summary>
    internal static Task RequireNoRefusedUpdateAsync(CollectionAttemptDbContext db, ActionDraftRow draft, CancellationToken cancellationToken) =>
        draft.RefusedCandidateId is null ? Task.CompletedTask : throw new StaleApprovalException(StaleApprovalMessage);

    private const string StaleApprovalMessage = "A newer update of this bill arrived while the record was rejected, so approving it now would publish old facts. " +
        "Do these steps. 1. If the record is still rejected, press Request changes. 2. Ask the owner to run the command: actions draft requeue --refused. " +
        "If the update failed, the owner runs actions draft requeue <candidate-id>. " +
        "If it is blocked, the owner runs actions draft requeue --blocked --code <block-code>. " +
        "3. Approve the new draft when it appears.";

    /// <summary>The settlement refused an update of a record that it read as rejected. Under the review key, this reads the status again.
    /// It returns false when a person changed the record first, so the worker may draft the update. The candidate of the revision itself is no newer update, so it sets no block.</summary>
    internal static async Task<bool> ConfirmRefusalAsync(CollectionAttemptDbContext db, string candidateId, CancellationToken cancellationToken)
    {
        if (await FindDraftOfCandidateAsync(db, candidateId, cancellationToken) is not { ReviewStatus: ReviewStatus.Rejected } draft) return false;
        if (candidateId != draft.CandidateId) draft.RefusedCandidateId = candidateId;
        return true;
    }

    /// <summary>An update with the same facts, or with no tracked official, settled. It frees the draft when it is not older than the refused update.</summary>
    internal static async Task ClearRefusalAsync(CollectionAttemptDbContext db, string candidateId, CancellationToken cancellationToken)
    {
        if (await FindDraftOfCandidateAsync(db, candidateId, cancellationToken) is not { RefusedCandidateId: { } refused } draft) return;
        if (refused == candidateId || await IsNewerAsync(db, candidateId, refused, cancellationToken)) draft.RefusedCandidateId = null;
    }

    /// <summary>True when a newer candidate of the bill exists now. The caller holds the review key.</summary>
    internal static async Task<bool> IsSupersededAsync(CollectionAttemptDbContext db, string candidateId, CancellationToken cancellationToken)
    {
        var candidate = await db.Set<ActionCandidateRow>().AsNoTracking().SingleOrDefaultAsync(row => row.CandidateId == candidateId, cancellationToken);
        return candidate is not null && await HasNewerCandidateAsync(db, new ActionDraftCandidate(candidate.CandidateId, string.Empty, candidate.SourceId,
            candidate.Congress, candidate.BillType, candidate.BillNumber, new DateTimeOffset(candidate.UpdateDateUtcTicks, TimeSpan.Zero),
            candidate.IncludesSummaries, candidate.IncludesSubjects), cancellationToken);
    }

    internal static async Task<bool> IsRejectedOrBlockedAsync(CollectionAttemptDbContext db, string candidateId, CancellationToken cancellationToken)
    {
        var candidate = await db.Set<ActionCandidateRow>().AsNoTracking().SingleOrDefaultAsync(row => row.CandidateId == candidateId, cancellationToken);
        return candidate is not null && await db.Set<ActionDraftRow>().AsNoTracking().AnyAsync(draft => (draft.RefusedCandidateId != null || draft.ReviewStatus == ReviewStatus.Rejected) &&
            draft.SourceId == candidate.SourceId && draft.Congress == candidate.Congress && draft.BillType == candidate.BillType &&
            draft.BillNumber == candidate.BillNumber, cancellationToken);
    }

    private static async Task<ActionDraftRow?> FindDraftOfCandidateAsync(CollectionAttemptDbContext db, string candidateId, CancellationToken cancellationToken)
    {
        var candidate = await db.Set<ActionCandidateRow>().AsNoTracking().SingleOrDefaultAsync(row => row.CandidateId == candidateId, cancellationToken);
        return candidate is null ? null : await db.Set<ActionDraftRow>().SingleOrDefaultAsync(draft => draft.SourceId == candidate.SourceId &&
            draft.Congress == candidate.Congress && draft.BillType == candidate.BillType && draft.BillNumber == candidate.BillNumber, cancellationToken);
    }

    private static async Task<bool> IsNewerAsync(CollectionAttemptDbContext db, string candidateId, string thanCandidateId, CancellationToken cancellationToken)
    {
        var than = await db.Set<ActionCandidateRow>().AsNoTracking().SingleOrDefaultAsync(row => row.CandidateId == thanCandidateId, cancellationToken);
        if (than is null) return false;
        var candidate = new ActionDraftCandidate(than.CandidateId, string.Empty, than.SourceId, than.Congress, than.BillType, than.BillNumber,
            new DateTimeOffset(than.UpdateDateUtcTicks, TimeSpan.Zero), than.IncludesSummaries, than.IncludesSubjects);
        return await NewerCandidates(db, candidate).AnyAsync(row => row.CandidateId == candidateId, cancellationToken);
    }

    public static async Task<ActionDraftRevision?> ReadCurrentAsync(CollectionAttemptDbContext db, string recordId,
        CancellationToken cancellationToken)
    {
        var json = await (from draft in db.Set<ActionDraftRow>().AsNoTracking()
                          join revision in db.Set<ActionRevisionRow>().AsNoTracking()
                              on new { draft.RecordId, Number = draft.CurrentRevisionNumber } equals new { revision.RecordId, Number = revision.RevisionNumber }
                          where draft.RecordId == recordId
                          select revision.RevisionJson).SingleOrDefaultAsync(cancellationToken);
        return json is null ? null : ReviewJson.Deserialize<ActionDraftRevision>(json, "action revision");
    }

    public static void Revised(ActionDraftRow draft, ActionDraftRevision revision)
    {
        draft.CandidateId = revision.CandidateId;
        ListedDraftRows.Set(draft, revision.EventOn(), revision.TrackedIssueIds, revision.OfficialIds());
    }

    private static ActionDraftRow NewDraft(ActionDraftRevision revision) => new()
    {
        RecordId = revision.RecordId,
        SourceId = revision.SourceId,
        Congress = revision.Congress,
        BillType = revision.BillType,
        BillNumber = revision.BillNumber,
        CreatedAtUtcTicks = revision.CreatedAtUtc.UtcTicks
    };
}
