using CivicLens.Application.Review;
using CivicLens.Application.Votes;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Votes;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Votes.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Votes;

internal static class VoteDraftRows
{
    internal static readonly ReviewRecords<VoteDraftRow, VoteRevisionRow, VoteReviewDecisionRow, VoteDraftRevision> Records =
        new(PostgresAdvisoryLock.VoteReview, "Vote", nameof(VoteDraftRow.RecordId), Revised,
            new(RequireNoRefusedUpdateAsync));

    public static async Task<string?> AppendAnalysisRevisionAsync(CollectionAttemptDbContext db, VoteDraftRevision revision, string runId,
        CancellationToken cancellationToken)
    {
        revision.ValidateShape();
        await PostgresAdvisoryLock.AcquireAsync(db, PostgresAdvisoryLock.VoteReview, cancellationToken);
        if (await HasNewerCandidateAsync(db, revision.SourceId, revision.Chamber, revision.Congress, revision.SessionNumber, revision.RollCallNumber,
                revision.CandidateUpdateDate.UtcTicks, cancellationToken))
            return VoteDraftErrorCodes.SupersededByNewerCandidate;
        var draft = await db.Set<VoteDraftRow>().SingleOrDefaultAsync(row => row.RecordId == revision.RecordId, cancellationToken);
        if ((draft?.CurrentRevisionNumber ?? 0) != revision.RevisionNumber - 1) return VoteDraftErrorCodes.RevisionConflict;
        if (draft?.ReviewStatus == ReviewStatus.Rejected)
        {
            await RecordRefusalAsync(db, draft, revision.CandidateId, cancellationToken);
            return VoteDraftErrorCodes.RecordRejected;
        }
        if (draft is null) db.Add(draft = NewDraft(revision));
        await VoteMemberLists.EnsureAsync(db, revision, cancellationToken);
        await Records.AppendCodeApprovedAsync(db, draft, revision, VoteRevisionRows.Create(revision, runId), await Records.ReadCycleAsync(db, draft, cancellationToken),
            () => ApprovalCheck.OfAsync(() => PostgresVoteReviewStore.ValidateApprovalAsync(db, revision, cancellationToken)));
        return null;
    }

    public static Task<bool> IsRejectedAsync(CollectionAttemptDbContext db, string recordId, CancellationToken cancellationToken) =>
        db.Set<VoteDraftRow>().AsNoTracking().AnyAsync(draft => draft.RecordId == recordId && draft.ReviewStatus == ReviewStatus.Rejected, cancellationToken);

    public static Task<bool> HasNewerCandidateAsync(CollectionAttemptDbContext db, string sourceId, Chamber chamber, int congress,
        int sessionNumber, int rollCallNumber, long updateDateUtcTicks, CancellationToken cancellationToken) =>
        NewerCandidates(db, sourceId, chamber, congress, sessionNumber, rollCallNumber, updateDateUtcTicks).AnyAsync(cancellationToken);

    private static IQueryable<VoteCandidateRow> NewerCandidates(CollectionAttemptDbContext db, string sourceId, Chamber chamber, int congress,
        int sessionNumber, int rollCallNumber, long updateDateUtcTicks) =>
        db.Set<VoteCandidateRow>().AsNoTracking().Where(newer => newer.SourceId == sourceId && newer.Chamber == chamber && newer.Congress == congress &&
            newer.SessionNumber == sessionNumber && newer.RollCallNumber == rollCallNumber && newer.UpdateDateUtcTicks > updateDateUtcTicks);

    /// <summary>Refuses a person approval while the draft names a refused newer update, because the revision holds old facts.</summary>
    private static Task RequireNoRefusedUpdateAsync(CollectionAttemptDbContext db, VoteDraftRow draft, CancellationToken cancellationToken) =>
        draft.RefusedCandidateId is null ? Task.CompletedTask : throw new StaleApprovalException(StaleApprovalMessage);

    private const string StaleApprovalMessage = "A newer update of this vote arrived while the record was rejected, so approving it now would publish old facts. " +
        "Do these steps. 1. If the record is still rejected, press Request changes. 2. Ask the owner to run the command: votes draft requeue --refused. " +
        "If that update is Failed or Blocked, the owner runs votes draft requeue <candidate-id> or votes draft requeue --blocked --code instead. " +
        "3. Approve the new draft when it appears.";

    /// <summary>Names the candidate on the rejected draft when it is newer than the candidate of the draft and than any candidate it already names.
    /// An older late update never sets it. The caller holds the review key.</summary>
    private static async Task RecordRefusalAsync(CollectionAttemptDbContext db, VoteDraftRow draft, string candidateId, CancellationToken cancellationToken)
    {
        if (!await IsNewerAsync(db, candidateId, draft.CandidateId, cancellationToken)) return;
        if (draft.RefusedCandidateId is not null && draft.RefusedCandidateId != candidateId &&
            !await IsNewerAsync(db, candidateId, draft.RefusedCandidateId, cancellationToken)) return;
        draft.RefusedCandidateId = candidateId;
    }

    /// <summary>The settlement refused an update of a record that it read as rejected. Under the review key, this reads the status again.
    /// It returns false when a person changed the record first, so the worker may draft the update.</summary>
    internal static async Task<bool> ConfirmRefusalAsync(CollectionAttemptDbContext db, string candidateId, CancellationToken cancellationToken)
    {
        if (await FindDraftOfCandidateAsync(db, candidateId, cancellationToken) is not { ReviewStatus: ReviewStatus.Rejected } draft) return false;
        await RecordRefusalAsync(db, draft, candidateId, cancellationToken);
        return true;
    }

    /// <summary>An update with the same facts, or with no tracked member, settled. It frees the draft when it is not older than the refused update.</summary>
    internal static async Task ClearRefusalAsync(CollectionAttemptDbContext db, string candidateId, CancellationToken cancellationToken)
    {
        if (await FindDraftOfCandidateAsync(db, candidateId, cancellationToken) is not { RefusedCandidateId: { } refused } draft) return;
        if (refused == candidateId || await IsNewerAsync(db, candidateId, refused, cancellationToken)) draft.RefusedCandidateId = null;
    }

    internal static async Task<bool> HoldsRefusalAsync(CollectionAttemptDbContext db, string candidateId, CancellationToken cancellationToken)
    {
        var candidate = await db.Set<VoteCandidateRow>().AsNoTracking().SingleOrDefaultAsync(row => row.CandidateId == candidateId, cancellationToken);
        return candidate is not null && await db.Set<VoteDraftRow>().AsNoTracking().AnyAsync(draft => draft.RefusedCandidateId != null &&
            draft.SourceId == candidate.SourceId && draft.Chamber == candidate.Chamber && draft.Congress == candidate.Congress &&
            draft.SessionNumber == candidate.SessionNumber && draft.RollCallNumber == candidate.RollCallNumber, cancellationToken);
    }

    private static async Task<VoteDraftRow?> FindDraftOfCandidateAsync(CollectionAttemptDbContext db, string candidateId, CancellationToken cancellationToken)
    {
        var candidate = await db.Set<VoteCandidateRow>().AsNoTracking().SingleOrDefaultAsync(row => row.CandidateId == candidateId, cancellationToken);
        return candidate is null ? null : await db.Set<VoteDraftRow>().SingleOrDefaultAsync(draft => draft.SourceId == candidate.SourceId &&
            draft.Chamber == candidate.Chamber && draft.Congress == candidate.Congress && draft.SessionNumber == candidate.SessionNumber &&
            draft.RollCallNumber == candidate.RollCallNumber, cancellationToken);
    }

    private static async Task<bool> IsNewerAsync(CollectionAttemptDbContext db, string candidateId, string thanCandidateId, CancellationToken cancellationToken)
    {
        var than = await db.Set<VoteCandidateRow>().AsNoTracking().SingleOrDefaultAsync(row => row.CandidateId == thanCandidateId, cancellationToken);
        return than is not null && await NewerCandidates(db, than.SourceId, than.Chamber, than.Congress, than.SessionNumber, than.RollCallNumber,
            than.UpdateDateUtcTicks).AnyAsync(row => row.CandidateId == candidateId, cancellationToken);
    }

    public static async Task<VoteDraftRevision?> ReadCurrentAsync(CollectionAttemptDbContext db, string recordId,
        CancellationToken cancellationToken)
    {
        var json = await (from draft in db.Set<VoteDraftRow>().AsNoTracking()
                          join revision in db.Set<VoteRevisionRow>().AsNoTracking()
                              on new { draft.RecordId, Number = draft.CurrentRevisionNumber } equals new { revision.RecordId, Number = revision.RevisionNumber }
                          where draft.RecordId == recordId
                          select revision.RevisionJson).SingleOrDefaultAsync(cancellationToken);
        return json is null ? null : ReviewJson.Deserialize<VoteDraftRevision>(json, "vote revision");
    }

    private static void Revised(VoteDraftRow draft, VoteDraftRevision revision)
    {
        draft.CandidateId = revision.CandidateId;
        ListedDraftRows.Set(draft, revision.VotedOn, revision.TrackedIssueIds, revision.OfficialIds());
    }

    private static VoteDraftRow NewDraft(VoteDraftRevision revision) => new()
    {
        RecordId = revision.RecordId,
        SourceId = revision.SourceId,
        Chamber = revision.Chamber,
        Congress = revision.Congress,
        SessionNumber = revision.SessionNumber,
        RollCallNumber = revision.RollCallNumber,
        CreatedAtUtcTicks = revision.CreatedAtUtc.UtcTicks
    };
}
