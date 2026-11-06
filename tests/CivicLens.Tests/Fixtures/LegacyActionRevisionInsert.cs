namespace CivicLens.Tests.Fixtures;

// The insert relaxes the foreign keys and leaves every revision column null, as a row from before the columns existed.
internal static class LegacyActionRevisionInsert
{
    public const string NullColumns = """
        headline = NULL, summary = NULL, text_origin = NULL, suggestion_headline = NULL, suggestion_summary = NULL, suggestion_run_id = NULL,
        suggestion_fingerprint = NULL, source_id = NULL, congress = NULL, bill_type = NULL, bill_number = NULL, candidate_id = NULL,
        candidate_update_utc_ticks = NULL, candidate_includes_summaries = NULL, candidate_includes_subjects = NULL, fingerprint = NULL,
        summary_version = NULL, summary_citation_extraction_id = NULL, summary_citation_start = NULL, summary_citation_length = NULL,
        policy_area = NULL, policy_area_citation_extraction_id = NULL, policy_area_citation_start = NULL, policy_area_citation_length = NULL,
        policy_area_id = NULL, bill_title = NULL, bill_citation_extraction_id = NULL, bill_citation_start = NULL, bill_citation_length = NULL,
        issue_revision_id = NULL, subjects_hash = NULL, issue_candidates_recorded = NULL, event_on = NULL, cosponsor_count = NULL, bill_line = NULL
        """;

    public static string Sql(string recordId, string revisionJson) => $$"""
        SET session_replication_role = replica;
        INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, review_state_version,
            review_status, created_at_utc_ticks, last_revised_at_utc_ticks, candidate_id)
        VALUES ('{{recordId}}', 'congress-bills', 119, 'hr', 7, 1, 0, 'AwaitingReview', 100, 100, '{{recordId}}');
        INSERT INTO action_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
        VALUES ('{{recordId}}', 1, 'auth0|editor', 'Person', NULL, '{{revisionJson}}', 100)
        """;
}
