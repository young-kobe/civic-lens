using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents.Persistence;
using CivicLens.Infrastructure.Review.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Review;

internal static class ReviewQueries
{
    public static IQueryable<DocumentComparisonRow> EligibleComparisons(CollectionAttemptDbContext db) =>
        db.Set<DocumentComparisonRow>().AsNoTracking().Where(comparison => comparison.HasChanges &&
            !db.Set<DocumentChangeDraftRow>().Any(draft => draft.ComparisonId == comparison.ComparisonId));

    public static IQueryable<DocumentChangeDraftRow> ApprovedDrafts(CollectionAttemptDbContext db) =>
        db.Set<DocumentChangeDraftRow>().AsNoTracking().Where(draft => draft.ReviewStatus == DocumentChangeReviewStatus.Approved);

    public static IQueryable<DocumentChangeDraftRow> NeedsActionDrafts(CollectionAttemptDbContext db) =>
        db.Set<DocumentChangeDraftRow>().AsNoTracking().Where(draft => draft.ReviewStatus != DocumentChangeReviewStatus.Approved);

    // Published state comes from the active release, not the draft, so this stays a read query.
    public const string UnpublishedApprovedDrafts = """
        WITH active AS (
            SELECT e ->> 'draftId' AS draft_id, (e ->> 'revisionNumber')::int AS revision_number
            FROM publication_releases r CROSS JOIN LATERAL jsonb_array_elements(r.records_json::jsonb) e
            WHERE r.is_active)
        SELECT d.draft_id AS "DraftId", d.current_revision_number AS "CurrentRevisionNumber",
               d.created_at_utc_ticks AS "CreatedAtUtcTicks", a.revision_number AS "PublishedRevisionNumber"
        FROM document_change_drafts d LEFT JOIN active a ON a.draft_id = d.draft_id
        WHERE d.review_status = 'Approved'
        """;
}
