using CivicLens.Infrastructure.Review;

namespace CivicLens.Infrastructure.Briefs;

internal static class BriefReviewQueries
{
    public const string UnpublishedApproved = $"""
        WITH {ReviewQueries.ActivePublishedRecords}
        SELECT d.record_id AS "RecordId", d.current_revision_number AS "CurrentRevisionNumber",
               d.created_at_utc_ticks AS "CreatedAtUtcTicks", a.revision_number AS "PublishedRevisionNumber"
        FROM brief_drafts d
        LEFT JOIN active a ON a.kind = 'brief' AND a.record_id = d.record_id
        WHERE d.review_status = 'Approved'
        """;
}
