using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Publication;

/// <summary>Writes release-local Home issue counts from the exact revisions selected for a release.</summary>
internal static class ReleaseIssueCounts
{
    private const string FillSql = """
        WITH selected AS (
            SELECT kind, record_id, revision_number
              FROM publication_record_states
             WHERE first_release <= {0} AND (last_release IS NULL OR last_release >= {0})
               AND kind IN ('vote', 'action')
        ), vote_counts AS (
            SELECT vi.issue_id, count(*) AS total
              FROM selected s
              JOIN vote_revisions v ON v.record_id = s.record_id AND v.revision_number = s.revision_number
              JOIN vote_revision_issues vi ON vi.record_id = v.record_id AND vi.revision_number = v.revision_number
             WHERE s.kind = 'vote' AND v.member_list_id IS NOT NULL AND v.congress = {1}
               AND v.kind = ANY({2}::text[]) AND vi.is_tracked
             GROUP BY vi.issue_id
        ), bill_counts AS (
            SELECT ai.issue_id, count(*) AS total
              FROM selected s
              JOIN action_revisions a ON a.record_id = s.record_id AND a.revision_number = s.revision_number
              JOIN action_revision_issues ai ON ai.record_id = a.record_id AND ai.revision_number = a.revision_number
             WHERE s.kind = 'action' AND a.congress = {1} AND a.headline IS NOT NULL AND a.event_on IS NOT NULL
               AND ai.is_tracked
               AND EXISTS (SELECT 1 FROM action_revision_sponsorships p
                            WHERE p.record_id = a.record_id AND p.revision_number = a.revision_number)
             GROUP BY ai.issue_id
        ), snapshots AS (
            SELECT names.issue_id, COALESCE(v.total, 0) AS votes, COALESCE(b.total, 0) AS bills
              FROM release_issues names
              LEFT JOIN vote_counts v ON v.issue_id = names.issue_id
              LEFT JOIN bill_counts b ON b.issue_id = names.issue_id
             WHERE names.release_number = {0} AND names.count_congress IS NULL
        )
        UPDATE release_issues i
           SET count_congress = {1}, substantive_votes = s.votes, bills = s.bills
          FROM snapshots s
         WHERE i.release_number = {0} AND i.issue_id = s.issue_id AND i.count_congress IS NULL
        """;

    public static Task<int> FillAsync(CollectionAttemptDbContext db, int releaseNumber, int? congress,
        CancellationToken cancellationToken)
    {
        if (congress is not { } selectedCongress) return Task.FromResult(0);
        return db.Database.ExecuteSqlRawAsync(FillSql,
            [releaseNumber, selectedCongress,
                CivicLens.Publication.Contracts.PublishedVoteKindExtensions.SubstantiveNames()], cancellationToken);
    }
}
