using CivicLens.Application.Review;
using CivicLens.Application.Votes;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
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
            new(RequireNoRefusedNewerCandidateAsync));

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
        if (draft?.ReviewStatus == ReviewStatus.Rejected) return VoteDraftErrorCodes.RecordRejected;
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

    /// <summary>Refuses to reverse a rejection when the worker refused a newer candidate, because the old revision holds old facts.
    /// The refusal shows in the <c>reason</c> column of the run.</summary>
    public static async Task RequireNoRefusedNewerCandidateAsync(CollectionAttemptDbContext db, VoteDraftRow draft, CancellationToken cancellationToken)
    {
        var current = await db.Set<VoteCandidateRow>().AsNoTracking().SingleOrDefaultAsync(row => row.CandidateId == draft.CandidateId, cancellationToken);
        if (current is null) return;
        var refused = await NewerCandidates(db, current.SourceId, current.Chamber, current.Congress, current.SessionNumber, current.RollCallNumber,
                current.UpdateDateUtcTicks)
            .AnyAsync(newer => db.Set<VoteAnalysisRunRow>().Any(run => run.CandidateId == newer.CandidateId && run.Task == VoteDraftTask.Task &&
                run.Reason == VoteDraftErrorCodes.RecordRejected), cancellationToken);
        if (refused) throw new ReversalRefusedException();
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
