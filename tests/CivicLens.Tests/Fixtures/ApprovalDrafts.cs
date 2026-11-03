namespace CivicLens.Tests.Fixtures;

/// <summary>One draft row of each published kind, written with SQL so the tests need none of the collection that feeds the draft tables.</summary>
internal static class ApprovalDrafts
{
    public const string RecordId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    public const string ShortId = "0123456789abcdef0123456789abcdef";

    public static readonly Dictionary<string, string> Inserts = new()
    {
        ["action_drafts"] = $"""
            INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, review_state_version, review_status, created_at_utc_ticks)
            VALUES ('{RecordId}', 'congress-gov', 119, 'hr', 1, 1, 0, 'AwaitingReview', 0)
            """,
        ["brief_drafts"] = $"""
            INSERT INTO brief_drafts (record_id, week_start, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES ('{RecordId}', DATE '2026-01-05', 1, 0, 'AwaitingReview', 0, 0)
            """,
        ["document_change_drafts"] = $"""
            INSERT INTO document_change_drafts (draft_id, comparison_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks)
            VALUES ('{ShortId}', 'comparison', 1, 0, 'AwaitingReview', 0)
            """,
        ["executive_action_drafts"] = $"""
            INSERT INTO executive_action_drafts (record_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES ('{RecordId}', 1, 0, 'AwaitingReview', 0, 0)
            """,
        ["relationship_drafts"] = $"""
            INSERT INTO relationship_drafts (record_id, current_revision_number, review_state_version, review_status, no_longer_matches, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES ('{RecordId}', 1, 0, 'AwaitingReview', false, 0, 0)
            """,
        ["statement_drafts"] = """
            INSERT INTO statement_drafts (candidate_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks)
            VALUES ('candidate', 1, 0, 'AwaitingReview', 0)
            """,
        ["vote_drafts"] = $"""
            INSERT INTO vote_drafts (record_id, source_id, chamber, congress, session_number, roll_call_number, current_revision_number, review_state_version, review_status, created_at_utc_ticks)
            VALUES ('{RecordId}', 'house-clerk', 'House', 119, 1, 1, 1, 0, 'AwaitingReview', 0)
            """
    };

    public static readonly Dictionary<string, (string Kind, string RecordId)> Records = new()
    {
        ["action_drafts"] = ("action", RecordId),
        ["brief_drafts"] = ("brief", RecordId),
        ["document_change_drafts"] = ("documentChange", ShortId),
        ["executive_action_drafts"] = ("executiveAction", RecordId),
        ["relationship_drafts"] = ("relationship", RecordId),
        ["statement_drafts"] = ("statement", "candidate"),
        ["vote_drafts"] = ("vote", RecordId)
    };

    public const string DropForeignKeys = """
        ALTER TABLE action_drafts DROP CONSTRAINT "FK_action_drafts_action_candidates_candidate_id";
        ALTER TABLE vote_drafts DROP CONSTRAINT "FK_vote_drafts_vote_candidates_candidate_id";
        ALTER TABLE document_change_drafts DROP CONSTRAINT "FK_document_change_drafts_document_comparisons_comparison_id";
        ALTER TABLE statement_drafts DROP CONSTRAINT "FK_statement_drafts_statement_candidates_candidate_id";
        """;
}
