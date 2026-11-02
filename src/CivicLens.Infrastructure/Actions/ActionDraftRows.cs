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
        if (draft?.ReviewStatus == ReviewStatus.Rejected) return ActionDraftErrorCodes.RecordRejected;
        if (draft is null) db.Add(draft = NewDraft(revision));
        var records = PostgresActionReviewStore.Records;
        await records.AppendCodeApprovedAsync(db, draft, revision, NewRevision(revision, runId), await records.ReadCycleAsync(db, draft, cancellationToken),
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

    /// <summary>Refuses to reverse a rejection when the worker refused a newer candidate, because the old revision holds old facts.
    /// The refusal shows in the <c>reason</c> column of the run.</summary>
    public static async Task RequireNoRefusedNewerCandidateAsync(CollectionAttemptDbContext db, ActionDraftRow draft, CancellationToken cancellationToken)
    {
        var current = await db.Set<ActionCandidateRow>().AsNoTracking().SingleOrDefaultAsync(row => row.CandidateId == draft.CandidateId, cancellationToken);
        if (current is null) return;
        var candidate = new ActionDraftCandidate(current.CandidateId, draft.RecordId, current.SourceId, current.Congress, current.BillType,
            current.BillNumber, new DateTimeOffset(current.UpdateDateUtcTicks, TimeSpan.Zero), current.IncludesSummaries, current.IncludesSubjects);
        var refused = await NewerCandidates(db, candidate).AnyAsync(newer => db.Set<ActionAnalysisRunRow>().Any(run =>
            run.CandidateId == newer.CandidateId && run.Task == ActionDraftTask.Task && run.Reason == ActionDraftErrorCodes.RecordRejected), cancellationToken);
        if (refused) throw new ReversalRefusedException();
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

    public static ActionRevisionRow NewRevision(ActionDraftRevision revision, string? runId) => new()
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
