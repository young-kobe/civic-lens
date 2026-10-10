namespace CivicLens.Infrastructure.Review;

internal static class ReviewQueries
{
    // JSON status 0 is DocumentComparisonStatus.Complete. The partial index ix_document_comparisons_eligible uses the same test.
    private const string CompleteWithChanges = """
        (c.result_json::jsonb ->> 'status') = '0' AND jsonb_array_length(c.result_json::jsonb -> 'hunks') > 0
        """;

    private const string WithoutDraft =
        "NOT EXISTS (SELECT 1 FROM document_change_drafts x WHERE x.comparison_id = c.comparison_id)";

    // The status is the kind of the newest decision on the current revision, exactly as DocumentChangeReviewPolicy reads it.
    private const string IsApproved = """
        COALESCE((SELECT r.kind FROM document_change_review_decisions r
                   WHERE r.draft_id = d.draft_id AND r.revision_number = d.current_revision_number
                   ORDER BY r.review_state_version DESC LIMIT 1) = 'Approve', false)
        """;

    public const string EligibleComparisons =
        "SELECT c.* FROM document_comparisons c WHERE " + CompleteWithChanges + " AND " + WithoutDraft;

    public const string ApprovedDrafts = "SELECT d.* FROM document_change_drafts d WHERE " + IsApproved;
    public const string NeedsActionDrafts = "SELECT d.* FROM document_change_drafts d WHERE NOT " + IsApproved;

    // Releases store their records as a JSON array of draft and revision bindings. The active release is the published state.
    public const string UnpublishedApprovedDrafts = """
        WITH active AS (
            SELECT e ->> 'draftId' AS draft_id, (e ->> 'revisionNumber')::int AS revision_number
            FROM publication_releases r CROSS JOIN LATERAL jsonb_array_elements(r.records_json::jsonb) e
            WHERE r.is_active)
        SELECT d.draft_id AS "DraftId", d.current_revision_number AS "CurrentRevisionNumber",
               d.created_at_utc_ticks AS "CreatedAtUtcTicks", a.revision_number AS "PublishedRevisionNumber"
        FROM document_change_drafts d LEFT JOIN active a ON a.draft_id = d.draft_id
        WHERE
        """ + " " + IsApproved;

    public const string Overview = "SELECT (SELECT count(*) FROM (" + EligibleComparisons + ") e)::int AS \"NewChanges\", " +
        "(SELECT count(*) FROM (" + NeedsActionDrafts + ") n)::int AS \"DraftsNeedingAction\", " +
        "(SELECT count(*) FROM (" + ApprovedDrafts + ") a)::int AS \"ApprovedDrafts\"";
}
