using CivicLens.Infrastructure.Review;

namespace CivicLens.Infrastructure.Relationships;

internal static class RelationshipReviewQueries
{
    public const string UnpublishedApproved = $"""
        WITH {ReviewQueries.ActivePublishedRecords}
        SELECT d.record_id AS "RecordId", d.current_revision_number AS "CurrentRevisionNumber",
               d.created_at_utc_ticks AS "CreatedAtUtcTicks", a.revision_number AS "PublishedRevisionNumber"
        FROM relationship_drafts d
        LEFT JOIN active a ON a.kind = 'relationship' AND a.record_id = d.record_id
        WHERE d.review_status = 'Approved'
        """;
}
