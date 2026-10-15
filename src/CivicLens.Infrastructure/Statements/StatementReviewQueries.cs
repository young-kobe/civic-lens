using CivicLens.Application.Statements;
using CivicLens.Infrastructure.Review;

namespace CivicLens.Infrastructure.Statements;

internal static class StatementReviewQueries
{
    public const string UnpublishedApproved = $"""
        WITH {ReviewQueries.ActivePublishedRecords}
        SELECT d.candidate_id AS "RecordId", d.current_revision_number AS "CurrentRevisionNumber",
               d.created_at_utc_ticks AS "CreatedAtUtcTicks", a.revision_number AS "PublishedRevisionNumber"
        FROM statement_drafts d
        LEFT JOIN active a ON a.kind = 'statement' AND a.record_id = d.candidate_id
        WHERE d.review_status = 'Approved'
        """;

    public const string NotInReview = $"""
        SELECT c.candidate_id AS "CandidateId", c.source_id AS "SourceId", c.url AS "Url",
               c.created_at_utc_ticks AS "CreatedAtUtcTicks", e.task AS "Stage", e.error_code AS "ReasonCode"
        FROM statement_candidates c
        CROSS JOIN LATERAL (
            SELECT a.task, a.error_code
            FROM statement_analysis a
            WHERE a.candidate_id = c.candidate_id
              AND (a.status IN ('Failed', 'Blocked') OR (a.status = 'Succeeded' AND a.error_code = '{StatementGateReasons.NoTrackedIssue}'))
            ORDER BY CASE a.task WHEN '{StatementDraftTask.Task}' THEN 0 ELSE 1 END
            LIMIT 1) e
        WHERE NOT EXISTS (SELECT 1 FROM statement_drafts d WHERE d.candidate_id = c.candidate_id)
        UNION ALL
        SELECT c.candidate_id, c.source_id, c.url, c.created_at_utc_ticks, '{StatementDraftTask.Task}', '{StatementDraftErrorCodes.OverByteCap}'
        FROM statement_candidates c
        JOIN statement_drafts d ON d.candidate_id = c.candidate_id
        WHERE d.current_revision_number = 0
        """;
}
