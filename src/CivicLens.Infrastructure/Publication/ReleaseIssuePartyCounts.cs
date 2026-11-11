using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Publication;

/// <summary>Writes the party line-up of the bills of each issue, once, in the release commit. It counts the bills that
/// <see cref="ReleaseIssueCounts"/> counts, by the party that the term of the sponsor held on the sponsorship date. The saved rows
/// must equal the expected rows, as in <see cref="ReleaseBillCounts"/>. An exact replay is accepted.
/// The party of each bill comes from lateral lookups on the key of the sponsorship table and of the term table. A join of two derived sets
/// ran for minutes on a fresh table that had no statistics, and these lookups do not depend on the estimates of the planner.</summary>
internal static class ReleaseIssuePartyCounts
{
    private const string ExpectedSql = $$"""
        DROP TABLE IF EXISTS pg_temp.expected_release_issue_party_lineups;
        DROP TABLE IF EXISTS pg_temp.expected_release_issue_party_bills;
        CREATE TEMP TABLE expected_release_issue_party_bills ON COMMIT DROP AS
        WITH bill_parties AS (
            SELECT e.issue_id, bill.party
              FROM {{ReleaseIssueCounts.EligibleBillsTable}} e
             CROSS JOIN LATERAL (
                SELECT CASE WHEN count(sponsor.position) > 0 AND count(sponsor.party) = count(sponsor.position)
                                  AND count(DISTINCT sponsor.party) = 1 THEN min(sponsor.party) END AS party
                  FROM (SELECT p.position, term.party
                          FROM action_revision_sponsorships p
                          LEFT JOIN LATERAL (
                              SELECT CASE WHEN count(DISTINCT held.party) = 1 AND count(held.party) = count(*) THEN min(held.party) END AS party
                                FROM (SELECT CASE WHEN btrim(t.party) <> '' THEN t.party END AS party
                                        FROM release_official_terms t
                                       WHERE t.release_number = {0} AND t.official_id = p.person_id
                                         AND t.start_on <= p.sponsored_on AND (t.end_on IS NULL OR t.end_on >= p.sponsored_on)) held) term ON TRUE
                         WHERE p.record_id = e.record_id AND p.revision_number = e.revision_number AND p.role = 'Sponsor') sponsor) bill
        )
        SELECT issue_id, party, count(*)::integer AS bills FROM bill_parties GROUP BY issue_id, party;

        CREATE TEMP TABLE expected_release_issue_party_lineups ON COMMIT DROP AS
        SELECT i.issue_id, coalesce(sum(b.bills) FILTER (WHERE b.party IS NULL), 0)::integer AS unknown_bills
          FROM release_issues i
          LEFT JOIN expected_release_issue_party_bills b ON b.issue_id = i.issue_id
         WHERE i.release_number = {0} AND i.count_congress = {1}
         GROUP BY i.issue_id;
        """;

    private const string ExistingSql = """
        SELECT EXISTS (SELECT 1 FROM release_issue_party_lineups WHERE release_number = {0})
            OR EXISTS (SELECT 1 FROM release_issue_party_counts WHERE release_number = {0}) AS "Value"
        """;

    private const string InsertLineupsSql = """
        INSERT INTO release_issue_party_lineups (release_number, issue_id, unknown_bills)
        SELECT {0}, issue_id, unknown_bills FROM expected_release_issue_party_lineups
        """;

    private const string InsertCountsSql = """
        INSERT INTO release_issue_party_counts (release_number, issue_id, party, bills)
        SELECT {0}, issue_id, party, bills FROM expected_release_issue_party_bills WHERE party IS NOT NULL
        """;

    private const string VerifySql = """
        SELECT (NOT EXISTS (
            (SELECT issue_id, unknown_bills FROM expected_release_issue_party_lineups
             EXCEPT SELECT issue_id, unknown_bills FROM release_issue_party_lineups WHERE release_number = {0})
            UNION ALL
            (SELECT issue_id, unknown_bills FROM release_issue_party_lineups WHERE release_number = {0}
             EXCEPT SELECT issue_id, unknown_bills FROM expected_release_issue_party_lineups)
        ) AND NOT EXISTS (
            (SELECT issue_id, party, bills FROM expected_release_issue_party_bills WHERE party IS NOT NULL
             EXCEPT SELECT issue_id, party, bills FROM release_issue_party_counts WHERE release_number = {0})
            UNION ALL
            (SELECT issue_id, party, bills FROM release_issue_party_counts WHERE release_number = {0}
             EXCEPT SELECT issue_id, party, bills FROM expected_release_issue_party_bills WHERE party IS NOT NULL)
        ) AND NOT EXISTS (
            SELECT 1 FROM release_issues i
              JOIN release_issue_party_lineups l ON l.release_number = i.release_number AND l.issue_id = i.issue_id
             WHERE i.release_number = {0}
               AND i.bills <> l.unknown_bills + coalesce((SELECT sum(c.bills) FROM release_issue_party_counts c
                                                           WHERE c.release_number = l.release_number AND c.issue_id = l.issue_id), 0)
        )) AS "Value"
        """;

    public static async Task FillAsync(CollectionAttemptDbContext db, int releaseNumber, int? congress,
        CancellationToken cancellationToken)
    {
        if (congress is not { } selectedCongress) return;
        await ReleaseIssueCounts.EnsureEligibleBillsAsync(db, releaseNumber, selectedCongress, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(ExpectedSql, [releaseNumber, selectedCongress], cancellationToken);
        var existing = await db.Database.SqlQueryRaw<bool>(ExistingSql, releaseNumber).SingleAsync(cancellationToken);
        if (existing)
        {
            if (!await IsExactAsync(db, releaseNumber, cancellationToken))
                throw new InvalidOperationException($"Release {releaseNumber} already has an incomplete or changed party line-up snapshot.");
            return;
        }
        await db.Database.ExecuteSqlRawAsync(InsertLineupsSql, [releaseNumber], cancellationToken);
        await db.Database.ExecuteSqlRawAsync(InsertCountsSql, [releaseNumber], cancellationToken);
        if (!await IsExactAsync(db, releaseNumber, cancellationToken))
            throw new InvalidOperationException($"Release {releaseNumber} party line-up snapshot differs from its published revisions.");
    }

    private static Task<bool> IsExactAsync(CollectionAttemptDbContext db, int releaseNumber, CancellationToken cancellationToken) =>
        db.Database.SqlQueryRaw<bool>(VerifySql, releaseNumber).SingleAsync(cancellationToken);
}
