using System.Collections.Immutable;
using CivicLens.Application.Votes;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Votes;

internal static class BillVoteDrafts
{
    public static async Task<ImmutableArray<BillVoteDraft>> ReadAsync(CollectionAttemptDbContext db, int congress, string billType,
        int billNumber, CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQuery<BillVoteRow>($"""
            SELECT d.record_id AS "RecordId", d.session_number AS "SessionNumber", d.roll_call_number AS "RollCallNumber",
                   d.event_on AS "EventOn", d.review_status AS "ReviewStatus", r.revision_json::jsonb ->> 'kind' AS "Kind"
            FROM vote_drafts d
            JOIN vote_revisions r ON r.record_id = d.record_id AND r.revision_number = d.current_revision_number
            WHERE EXISTS (SELECT 1 FROM house_vote_candidates c WHERE c.candidate_id = d.candidate_id
                          AND c.congress = {congress} AND c.bill_type = {billType} AND c.bill_number = {billNumber})
            ORDER BY d.event_on DESC, d.session_number DESC, d.roll_call_number DESC, d.record_id
            LIMIT {BillVoteDraft.MaximumPerBill}
            """).ToArrayAsync(cancellationToken);
        return [.. rows.Select(row => new BillVoteDraft(row.RecordId, row.SessionNumber, row.RollCallNumber,
            Enum.Parse<VoteKind>(row.Kind), row.EventOn, Enum.Parse<ReviewStatus>(row.ReviewStatus)))];
    }

    private sealed record BillVoteRow(string RecordId, int SessionNumber, int RollCallNumber, DateOnly EventOn, string ReviewStatus, string Kind);
}
