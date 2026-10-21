using CivicLens.Infrastructure.Review;

namespace CivicLens.Infrastructure.Actions;

internal static class ActionReviewQueries
{
    public const string UnpublishedApproved = $"""
        WITH {ReviewQueries.ActivePublishedRecords}
        SELECT d.record_id AS "RecordId", d.current_revision_number AS "CurrentRevisionNumber",
               d.created_at_utc_ticks AS "CreatedAtUtcTicks", a.revision_number AS "PublishedRevisionNumber"
        FROM action_drafts d
        LEFT JOIN active a ON a.kind = 'action' AND a.record_id = d.record_id
        WHERE d.review_status = 'Approved'
        """;

    public static readonly string CodeApprovedUnpublished = ReviewQueries.CodeApprovedOnly(UnpublishedApproved, "action_review_decisions");
}
