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
        if (draft is null) db.Add(draft = NewDraft(revision));
        var records = PostgresActionReviewStore.Records;
        await records.AppendCodeApprovedAsync(db, draft, revision, NewRevision(revision, runId), await records.ReadCycleAsync(db, draft, cancellationToken),
            () => PassesApprovalChecksAsync(db, revision, cancellationToken), PostgresActionReviewStore.NewDecisionRow);
        return null;
    }

    private static async Task<bool> PassesApprovalChecksAsync(CollectionAttemptDbContext db, ActionDraftRevision revision,
        CancellationToken cancellationToken) =>
        (await ApprovalCheck.OfAsync(() => PostgresActionReviewStore.ValidateApprovalAsync(db, revision, cancellationToken))).Passed;

    public static Task<bool> HasNewerCandidateAsync(CollectionAttemptDbContext db, ActionDraftCandidate candidate, CancellationToken cancellationToken)
    {
        return db.Set<ActionCandidateRow>().AsNoTracking().Where(newer => newer.SourceId == candidate.SourceId &&
                newer.Congress == candidate.Congress && newer.BillType == candidate.BillType && newer.BillNumber == candidate.BillNumber)
            .Where(ActionCandidateInputs.Supersedes(candidate.UpdateDate.UtcTicks, candidate.IncludesSummaries, candidate.IncludesSubjects))
            .AnyAsync(cancellationToken);
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
        draft.LastRevisedAtUtcTicks = revision.CreatedAtUtc.UtcTicks;
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
