using CivicLens.Application.Relationships;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Relationships.Persistence;
using CivicLens.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Relationships;

internal static class RelationshipDraftRows
{
    internal static readonly ReviewRecords<RelationshipDraftRow, RelationshipRevisionRow, RelationshipReviewDecisionRow, RelationshipRevision> Records =
        new(PostgresAdvisoryLock.RelationshipReview, "Relationship", nameof(RelationshipDraftRow.RecordId),
            (draft, revision) => draft.LastRevisedAtUtcTicks = revision.CreatedAtUtc.UtcTicks);

    public static async Task<string?> AppendAnalysisRevisionAsync(CollectionAttemptDbContext db, RelationshipRevision revision, string runId,
        CancellationToken cancellationToken)
    {
        revision.ValidateShape();
        await PostgresAdvisoryLock.AcquireAsync(db, PostgresAdvisoryLock.RelationshipReview, cancellationToken);
        var draft = await db.Set<RelationshipDraftRow>().SingleOrDefaultAsync(row => row.RecordId == revision.RecordId, cancellationToken);
        if ((draft?.CurrentRevisionNumber ?? 0) != revision.RevisionNumber - 1) return RelationshipDraftErrorCodes.RevisionConflict;
        if (draft?.ReviewStatus == ReviewStatus.Approved) return RelationshipDraftErrorCodes.AlreadyApproved;
        if (draft is not null && await KeepsPersonEditAsync(db, revision, cancellationToken)) return RelationshipDraftErrorCodes.PersonEditKept;
        if (draft is null) db.Add(draft = NewDraft(revision));
        Records.AppendRevision(db, draft, revision, NewRevision(revision, runId), await Records.ReadCycleAsync(db, draft, cancellationToken));
        return null;
    }

    private static async Task<bool> KeepsPersonEditAsync(CollectionAttemptDbContext db, RelationshipRevision revision, CancellationToken cancellationToken) =>
        await ReadCurrentAsync(db, revision.RecordId, cancellationToken) is { } current &&
        current.IsPersonRevisionCiting(revision.Statement, revision.Target, revision.OfficialId);

    public static async Task<RelationshipRevision?> ReadCurrentAsync(CollectionAttemptDbContext db, string recordId,
        CancellationToken cancellationToken)
    {
        var json = await (from draft in db.Set<RelationshipDraftRow>().AsNoTracking()
                          join revision in db.Set<RelationshipRevisionRow>().AsNoTracking()
                              on new { draft.RecordId, Number = draft.CurrentRevisionNumber } equals new { revision.RecordId, Number = revision.RevisionNumber }
                          where draft.RecordId == recordId
                          select revision.RevisionJson).SingleOrDefaultAsync(cancellationToken);
        return json is null ? null : ReviewJson.Deserialize<RelationshipRevision>(json, "relationship revision");
    }

    private static RelationshipDraftRow NewDraft(RelationshipRevision revision) => new()
    {
        RecordId = revision.RecordId,
        CreatedAtUtcTicks = revision.CreatedAtUtc.UtcTicks
    };

    public static RelationshipRevisionRow NewRevision(RelationshipRevision revision, string? runId) => new()
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
