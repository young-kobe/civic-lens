using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Fixtures;

// The inserts relax the foreign keys, so a test can place a draft row of any kind without its parents.
internal static class DraftRowInserts
{
    public static string For(PublishedRecordKind kind) => "SET session_replication_role = replica;\n" + kind switch
    {
        PublishedRecordKind.DocumentChange => """
            INSERT INTO document_change_drafts (draft_id, comparison_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks)
            VALUES (@id, repeat('c', 64), 2, 0, 'AwaitingReview', 0)
            """,
        PublishedRecordKind.Statement => """
            INSERT INTO statement_drafts (candidate_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks)
            VALUES (@id, 2, 0, 'AwaitingReview', 0)
            """,
        PublishedRecordKind.Action => """
            INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, created_at_utc_ticks, last_revised_at_utc_ticks, review_state_version, review_status)
            VALUES (@id, 'congress-bills', 119, 'hr', 7, 2, 0, 0, 0, 'AwaitingReview')
            """,
        PublishedRecordKind.Vote => """
            INSERT INTO vote_drafts (record_id, source_id, chamber, congress, session_number, roll_call_number, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES (@id, 'congress-house-votes', 'House', 119, 1, 100, 2, 0, 'AwaitingReview', 0, 0)
            """,
        PublishedRecordKind.Brief => """
            INSERT INTO brief_drafts (record_id, week_start, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES (@id, '2026-01-05', 2, 0, 'AwaitingReview', 0, 0)
            """,
        PublishedRecordKind.Relationship => """
            INSERT INTO relationship_drafts (record_id, current_revision_number, review_state_version, review_status, no_longer_matches, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES (@id, 2, 0, 'AwaitingReview', false, 0, 0)
            """,
        PublishedRecordKind.ExecutiveAction => """
            INSERT INTO executive_action_drafts (record_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES (@id, 2, 0, 'AwaitingReview', 0, 0)
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Record kind has no draft insert.")
    };
}
