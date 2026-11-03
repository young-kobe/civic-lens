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

    /// <summary>Refuses a person approval while the worker has refused a newer candidate, because the revision holds old facts. The refusal
    /// shows in the <c>reason</c> column of the latest run of that candidate.</summary>
    private static async Task RequireNoRefusedNewerCandidateAsync(CollectionAttemptDbContext db, VoteDraftRow draft, CancellationToken cancellationToken)
    {
        var current = await db.Set<VoteCandidateRow>().AsNoTracking().SingleOrDefaultAsync(row => row.CandidateId == draft.CandidateId, cancellationToken);
        if (current is null) return;
        var newer = await NewerCandidates(db, current.SourceId, current.Chamber, current.Congress, current.SessionNumber, current.RollCallNumber,
            current.UpdateDateUtcTicks).Select(row => row.CandidateId).ToArrayAsync(cancellationToken);
        if ((await RefusedCandidateIdsAsync(db, newer, cancellationToken)).Length > 0) throw new StaleApprovalException(StaleApprovalMessage);
    }

    private const string StaleApprovalMessage = "A newer update of this vote arrived while the record was rejected, so approving it now would publish old facts. " +
        "Do these steps. 1. If the record is still rejected, press Request changes. 2. Ask the owner to run the command: votes draft requeue --refused. " +
        "3. Approve the new draft when it appears.";

    /// <summary>The candidates whose latest run was refused because the record was rejected. A later run clears the mark.</summary>
    internal static Task<string[]> RefusedCandidateIdsAsync(CollectionAttemptDbContext db, string[] candidateIds, CancellationToken cancellationToken) =>
        AnalysisRunLookup.Latest<VoteAnalysisRunRow>(db, VoteDraftTask.Task, nameof(VoteAnalysisRunRow.CandidateId), candidateIds)
            .Where(run => run.Reason == VoteDraftErrorCodes.RecordRejected).Select(run => run.CandidateId).ToArrayAsync(cancellationToken);

    /// <summary>True while the draft of the roll call of this candidate is rejected.</summary>
    internal static Task<bool> IsRejectedForCandidateAsync(CollectionAttemptDbContext db, string candidateId, CancellationToken cancellationToken) =>
        db.Set<VoteDraftRow>().AsNoTracking().AnyAsync(draft => draft.ReviewStatus == ReviewStatus.Rejected &&
            db.Set<VoteCandidateRow>().Any(candidate => candidate.CandidateId == candidateId && candidate.SourceId == draft.SourceId &&
                candidate.Chamber == draft.Chamber && candidate.Congress == draft.Congress && candidate.SessionNumber == draft.SessionNumber &&
                candidate.RollCallNumber == draft.RollCallNumber), cancellationToken);

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
