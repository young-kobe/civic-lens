using CivicLens.Application.Briefs;
using CivicLens.Core.Briefs;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Briefs.Persistence;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Briefs;

internal static class BriefDraftRows
{
    private static readonly ReviewRecords<BriefDraftRow, BriefRevisionRow, BriefReviewDecisionRow, BriefRevision> Records =
        new(PostgresAdvisoryLock.BriefReview, "Brief", nameof(BriefDraftRow.RecordId),
            (draft, revision) => draft.LastRevisedAtUtcTicks = revision.CreatedAtUtc.UtcTicks);

    public static async Task<string?> AppendAnalysisRevisionAsync(CollectionAttemptDbContext db, BriefRevision revision, string runId,
        CancellationToken cancellationToken)
    {
        revision.ValidateShape();
        await PostgresAdvisoryLock.AcquireAsync(db, PostgresAdvisoryLock.BriefReview, cancellationToken);
        var draft = await db.Set<BriefDraftRow>().SingleOrDefaultAsync(row => row.RecordId == revision.RecordId, cancellationToken);
        if ((draft?.CurrentRevisionNumber ?? 0) != revision.RevisionNumber - 1) return BriefDraftErrorCodes.RevisionConflict;
        if (draft is null) db.Add(draft = NewDraft(revision));
        Records.AppendRevision(db, draft, revision, NewRevision(revision, runId), await Records.ReadCycleAsync(db, draft, cancellationToken));
        return null;
    }

    public static async Task<BriefRevision?> ReadCurrentAsync(CollectionAttemptDbContext db, string recordId, CancellationToken cancellationToken)
    {
        var json = await (from draft in db.Set<BriefDraftRow>().AsNoTracking()
                          join revision in db.Set<BriefRevisionRow>().AsNoTracking()
                              on new { draft.RecordId, Number = draft.CurrentRevisionNumber } equals new { revision.RecordId, Number = revision.RevisionNumber }
                          where draft.RecordId == recordId
                          select revision.RevisionJson).SingleOrDefaultAsync(cancellationToken);
        return json is null ? null : ReviewJson.Deserialize<BriefRevision>(json, "brief revision");
    }

    private static BriefDraftRow NewDraft(BriefRevision revision) => new()
    {
        RecordId = revision.RecordId,
        WeekStart = revision.Window.WeekStart,
        CreatedAtUtcTicks = revision.CreatedAtUtc.UtcTicks
    };

    private static BriefRevisionRow NewRevision(BriefRevision revision, string runId) => new()
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
