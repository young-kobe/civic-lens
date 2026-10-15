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

    public const string ActivePublishedRecords = """
        active AS (
            SELECT COALESCE(e ->> 'kind', 'documentChange') AS kind, COALESCE(e ->> 'recordId', e ->> 'draftId') AS record_id,
                   (e ->> 'revisionNumber')::int AS revision_number
            FROM publication_releases r CROSS JOIN LATERAL jsonb_array_elements(r.records_json::jsonb) e
            WHERE r.is_active)
        """;

    // Published state comes from the active release, not the draft, so this stays a read query.
    public const string UnpublishedApprovedDrafts = $"""
        WITH {ActivePublishedRecords}
        SELECT d.draft_id AS "RecordId", d.current_revision_number AS "CurrentRevisionNumber",
               d.created_at_utc_ticks AS "CreatedAtUtcTicks", a.revision_number AS "PublishedRevisionNumber"
        FROM document_change_drafts d
        LEFT JOIN active a ON a.kind = 'documentChange' AND a.record_id = d.draft_id
        WHERE d.review_status = 'Approved'
        """;
}
