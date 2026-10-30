using CivicLens.Publication.Contracts;

namespace CivicLens.Infrastructure.Review;

internal static class ReviewTables
{
    public static (string Revisions, string Decisions, string IdColumn) For(PublishedRecordKind kind) => kind switch
    {
        PublishedRecordKind.DocumentChange => ("document_change_revisions", "document_change_review_decisions", "draft_id"),
        PublishedRecordKind.Statement => ("statement_revisions", "statement_review_decisions", "candidate_id"),
        PublishedRecordKind.Action => ("action_revisions", "action_review_decisions", "record_id"),
        PublishedRecordKind.Vote => ("vote_revisions", "vote_review_decisions", "record_id"),
        PublishedRecordKind.ExecutiveAction => ("executive_action_revisions", "executive_action_review_decisions", "record_id"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Record kind has no revision tables.")
    };
}
