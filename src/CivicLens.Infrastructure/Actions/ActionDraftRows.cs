using CivicLens.Core.Actions;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Actions.Persistence;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Actions;

internal static class ActionDraftRows
{
    public static async Task<bool> TryAppendAnalysisRevisionAsync(CollectionAttemptDbContext db, ActionDraftRevision revision, string runId,
        CancellationToken cancellationToken)
    {
        revision.ValidateShape();
        await ReviewWriteLock.AcquireAsync(db, ReviewWriteLock.Action, cancellationToken);
        var draft = await db.Set<ActionDraftRow>().SingleOrDefaultAsync(row => row.RecordId == revision.RecordId, cancellationToken);
        if ((draft?.CurrentRevisionNumber ?? 0) != revision.RevisionNumber - 1) return false;
        if (draft is null) db.Add(NewDraft(revision));
        else draft.CurrentRevisionNumber = revision.RevisionNumber;
        db.Add(NewRevision(revision, runId));
        return true;
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

    private static ActionDraftRow NewDraft(ActionDraftRevision revision) => new()
    {
        RecordId = revision.RecordId,
        SourceId = revision.SourceId,
        Congress = revision.Congress,
        BillType = revision.BillType,
        BillNumber = revision.BillNumber,
        CurrentRevisionNumber = revision.RevisionNumber,
        CreatedAtUtcTicks = revision.CreatedAtUtc.UtcTicks
    };

    private static ActionRevisionRow NewRevision(ActionDraftRevision revision, string runId) => new()
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
