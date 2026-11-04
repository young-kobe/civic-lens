namespace CivicLens.Tests.Fixtures;

// The insert relaxes the foreign keys and leaves every revision column null, as a row from before the columns existed.
internal static class LegacyActionRevisionInsert
{
    public static string Sql(string recordId, string revisionJson) => $$"""
        SET session_replication_role = replica;
        INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, review_state_version,
            review_status, created_at_utc_ticks, last_revised_at_utc_ticks, candidate_id)
        VALUES ('{{recordId}}', 'congress-bills', 119, 'hr', 7, 1, 0, 'AwaitingReview', 100, 100, '{{recordId}}');
        INSERT INTO action_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
        VALUES ('{{recordId}}', 1, 'auth0|editor', 'Person', NULL, '{{revisionJson}}', 100)
        """;
}
