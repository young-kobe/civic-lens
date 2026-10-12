using CivicLens.Application.Statements;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Statements.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Statements;

internal static class StatementReviewQueries
{
    public static IQueryable<StatementDraftRow> ApprovedDrafts(CollectionAttemptDbContext db) =>
        db.Set<StatementDraftRow>().AsNoTracking().Where(draft => draft.ReviewStatus == ReviewStatus.Approved);

    public static IQueryable<StatementDraftRow> NeedsActionDrafts(CollectionAttemptDbContext db) =>
        db.Set<StatementDraftRow>().AsNoTracking().Where(draft => draft.ReviewStatus != ReviewStatus.Approved);

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
