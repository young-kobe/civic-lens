using CivicLens.Application.Votes;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.HouseVotes;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Votes.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Votes;

internal static class VoteDraftRows
{
    internal static readonly ReviewRecords<VoteDraftRow, VoteRevisionRow, VoteReviewDecisionRow, VoteDraftRevision> Records =
        new(PostgresAdvisoryLock.VoteReview, "Vote", nameof(VoteDraftRow.RecordId), Revised);

    public static async Task<string?> AppendAnalysisRevisionAsync(CollectionAttemptDbContext db, VoteDraftRevision revision, string runId,
        CancellationToken cancellationToken)
    {
        revision.ValidateShape();
        await PostgresAdvisoryLock.AcquireAsync(db, PostgresAdvisoryLock.VoteReview, cancellationToken);
        if (await HasNewerCandidateAsync(db, revision.SourceId, revision.Congress, revision.SessionNumber, revision.RollCallNumber,
                revision.CandidateUpdateDate.UtcTicks, cancellationToken))
            return VoteDraftErrorCodes.SupersededByNewerCandidate;
        var draft = await db.Set<VoteDraftRow>().SingleOrDefaultAsync(row => row.RecordId == revision.RecordId, cancellationToken);
        if ((draft?.CurrentRevisionNumber ?? 0) != revision.RevisionNumber - 1) return VoteDraftErrorCodes.RevisionConflict;
        if (draft is null) db.Add(draft = NewDraft(revision));
        Records.AppendRevision(db, draft, revision, NewRevision(revision, runId), await Records.ReadCycleAsync(db, draft, cancellationToken));
        return null;
    }

    public static Task<bool> HasNewerCandidateAsync(CollectionAttemptDbContext db, string sourceId, int congress, int sessionNumber,
        int rollCallNumber, long updateDateUtcTicks, CancellationToken cancellationToken) =>
        db.Set<HouseVoteCandidateRow>().AsNoTracking().AnyAsync(newer => newer.SourceId == sourceId && newer.Congress == congress &&
            newer.SessionNumber == sessionNumber && newer.RollCallNumber == rollCallNumber && newer.UpdateDateUtcTicks > updateDateUtcTicks,
            cancellationToken);

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
        draft.LastRevisedAtUtcTicks = revision.CreatedAtUtc.UtcTicks;
        draft.CandidateId = revision.CandidateId;
        ListedDraftRows.Set(draft, revision.VotedOn, revision.TrackedIssueIds, revision.OfficialIds());
    }

    private static VoteDraftRow NewDraft(VoteDraftRevision revision) => new()
    {
        RecordId = revision.RecordId,
        SourceId = revision.SourceId,
        Congress = revision.Congress,
        SessionNumber = revision.SessionNumber,
        RollCallNumber = revision.RollCallNumber,
        CreatedAtUtcTicks = revision.CreatedAtUtc.UtcTicks
    };

    public static VoteRevisionRow NewRevision(VoteDraftRevision revision, string? runId) => new()
    {
        RecordId = revision.RecordId,
        RevisionNumber = revision.RevisionNumber,
        AuthorSubject = revision.AuthorSubject,
        AuthorKind = ReviewAuthor.KindOf(revision.AuthorSubject),
        RunId = runId,
        RevisionJson = ReviewJson.Serialize(revision),
        CreatedAtUtcTicks = revision.CreatedAtUtc.UtcTicks
    };
}
