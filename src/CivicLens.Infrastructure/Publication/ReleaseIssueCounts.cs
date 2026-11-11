using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Publication;

/// <summary>Writes release-local Home issue counts from the exact revisions selected for a release.</summary>
internal static class ReleaseIssueCounts
{
    /// <summary>The temp table that holds the eligible bills of a release. <see cref="ReleaseIssuePartyCounts"/> reads it too.</summary>
    internal const string EligibleBillsTable = "expected_release_issue_eligible_bills";

    private const string SelectedCte = """
        selected AS (
            SELECT kind, record_id, revision_number
              FROM publication_record_states
             WHERE first_release <= {0} AND (last_release IS NULL OR last_release >= {0})
               AND kind IN ('vote', 'action')
        )
        """;

    /// <summary>One row for each selected bill revision and tracked issue that the Home tile counts. The line-up of parties classifies the same rows.
    /// Placeholders: {0} is the release number and {1} is the Congress.</summary>
    private const string CreateEligibleBillsSql = $$"""
        DROP TABLE IF EXISTS pg_temp.{{EligibleBillsTable}};
        CREATE TEMP TABLE {{EligibleBillsTable}} ON COMMIT DROP AS
        WITH {{SelectedCte}}
        SELECT ai.issue_id, a.record_id, a.revision_number
          FROM selected s
          JOIN action_revisions a ON a.record_id = s.record_id AND a.revision_number = s.revision_number
          JOIN action_revision_issues ai ON ai.record_id = a.record_id AND ai.revision_number = a.revision_number
         WHERE s.kind = 'action' AND a.congress = {1} AND a.headline IS NOT NULL AND a.event_on IS NOT NULL
           AND ai.is_tracked
           AND EXISTS (SELECT 1 FROM action_revision_sponsorships p
                        WHERE p.record_id = a.record_id AND p.revision_number = a.revision_number)
        """;

    private const string EligibleBillsExistSql = $"""
        SELECT to_regclass('pg_temp.{EligibleBillsTable}') IS NOT NULL AS "Value"
        """;

    private const string FillSql = $$"""
        {{CreateEligibleBillsSql}};
        WITH {{SelectedCte}}, vote_counts AS (
            SELECT vi.issue_id, count(*) AS total
              FROM selected s
              JOIN vote_revisions v ON v.record_id = s.record_id AND v.revision_number = s.revision_number
              JOIN vote_revision_issues vi ON vi.record_id = v.record_id AND vi.revision_number = v.revision_number
             WHERE s.kind = 'vote' AND v.member_list_id IS NOT NULL AND v.congress = {1}
               AND v.kind = ANY({2}::text[]) AND vi.is_tracked
             GROUP BY vi.issue_id
        ), bill_counts AS (
            SELECT issue_id, count(*) AS total FROM {{EligibleBillsTable}} GROUP BY issue_id
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

    /// <summary>Gives the line-up of parties the eligible bills that <see cref="FillAsync"/> built in the same transaction. Outside a transaction,
    /// the table is already gone, and this call builds it again.</summary>
    internal static async Task EnsureEligibleBillsAsync(CollectionAttemptDbContext db, int releaseNumber, int congress,
        CancellationToken cancellationToken)
    {
        if (await db.Database.SqlQueryRaw<bool>(EligibleBillsExistSql).SingleAsync(cancellationToken)) return;
        await db.Database.ExecuteSqlRawAsync(CreateEligibleBillsSql, [releaseNumber, congress], cancellationToken);
    }

    /// <summary>Builds the eligible bills, writes the counts, and keeps the table for the rest of the commit transaction.</summary>
    public static Task<int> FillAsync(CollectionAttemptDbContext db, int releaseNumber, int? congress,
        CancellationToken cancellationToken)
    {
        if (congress is not { } selectedCongress) return Task.FromResult(0);
        return db.Database.ExecuteSqlRawAsync(FillSql,
            [releaseNumber, selectedCongress,
                CivicLens.Publication.Contracts.PublishedVoteKindExtensions.SubstantiveNames()], cancellationToken);
    }
}
