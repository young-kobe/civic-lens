using CivicLens.Application.Publication;
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
    private const string DropExpectedSql = """
        DROP TABLE IF EXISTS pg_temp.expected_release_issue_party_lineups;
        DROP TABLE IF EXISTS pg_temp.expected_release_issue_party_bills;
        """;

    /// <summary>The rule of the line-up, once. One row for each issue and party with the count of bills. A null party is the unknown count.
    /// It counts only issues that hold counts for the Congress, because every party row needs its header row. It reads the eligible bills by the name <see cref="ReleaseIssueCounts.EligibleBillsTable"/>, which is a temp table in the commit and a CTE in the verifier.</summary>
    private const string ExpectedBillsSelect = $$"""
        WITH bill_parties AS (
            SELECT e.issue_id, bill.party
              FROM {{ReleaseIssueCounts.EligibleBillsTable}} e
              JOIN release_issues counted ON counted.release_number = {0} AND counted.issue_id = e.issue_id AND counted.count_congress = {1}
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
        SELECT issue_id, party, count(*)::integer AS bills FROM bill_parties GROUP BY issue_id, party
        """;

    /// <summary>One header row for each issue of the release that has counts for this Congress, with the unknown count.</summary>
    private const string ExpectedLineupsSelect = """
        SELECT i.issue_id, coalesce(sum(b.bills) FILTER (WHERE b.party IS NULL), 0)::integer AS unknown_bills
          FROM release_issues i
          LEFT JOIN expected_release_issue_party_bills b ON b.issue_id = i.issue_id
         WHERE i.release_number = {0} AND i.count_congress = {1}
         GROUP BY i.issue_id
        """;

    private const string ExpectedSql = $"""
        CREATE TEMP TABLE expected_release_issue_party_bills ON COMMIT DROP AS
        {ExpectedBillsSelect};

        CREATE TEMP TABLE expected_release_issue_party_lineups ON COMMIT DROP AS
        {ExpectedLineupsSelect};
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

    /// <summary>True when the saved rows equal the expected rows in both directions and the parts add up to the Home tile base.</summary>
    private const string ExactCondition = """
        NOT EXISTS (
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
        )
        """;

    private const string VerifySql = $"""
        SELECT ({ExactCondition}) AS "Value"
        """;

    /// <summary>The read-only check. It computes the expected rows as CTEs, because a read-only transaction cannot create tables with a query.
    /// It returns 0 for a verified release, 1 when expected rows are not saved, and 2 when saved rows differ.</summary>
    private const string VerifyStatusSql = $$"""
        WITH {{ReleaseIssueCounts.EligibleBillsTable}} AS ({{ReleaseIssueCounts.EligibleBillsSelectSql}}),
             expected_release_issue_party_bills AS ({{ExpectedBillsSelect}}),
             expected_release_issue_party_lineups AS ({{ExpectedLineupsSelect}})
        SELECT CASE
                 WHEN EXISTS (SELECT 1 FROM release_issue_party_lineups WHERE release_number = {0})
                   OR EXISTS (SELECT 1 FROM release_issue_party_counts WHERE release_number = {0})
                   THEN CASE WHEN {{ExactCondition}} THEN 0 ELSE 2 END
                 WHEN EXISTS (SELECT 1 FROM expected_release_issue_party_lineups) THEN 1
                 ELSE 0
               END AS "Value"
        """;

    private const string ExpectedAnyIssueSql = """
        SELECT EXISTS (SELECT 1 FROM expected_release_issue_party_lineups) AS "Value"
        """;

    /// <summary>Writes the line-up of a release once. It returns true when it wrote rows. It returns false for an exact replay and for a release that has no
    /// Congress or no counted issue. A changed or incomplete snapshot fails and nothing changes.</summary>
    public static async Task<bool> FillAsync(CollectionAttemptDbContext db, int releaseNumber, int? congress,
        CancellationToken cancellationToken)
    {
        if (congress is not { } selectedCongress) return false;
        await db.Database.ExecuteSqlRawAsync(DropExpectedSql, cancellationToken);
        await PrepareExpectedAsync(db, releaseNumber, selectedCongress, cancellationToken);
        if (await ExistsAsync(db, releaseNumber, cancellationToken))
        {
            if (!await IsExactAsync(db, releaseNumber, cancellationToken))
                throw new BillCountRefusedException($"Release {releaseNumber} already has an incomplete or changed party line-up snapshot.");
            return false;
        }
        await db.Database.ExecuteSqlRawAsync(InsertLineupsSql, [releaseNumber], cancellationToken);
        await db.Database.ExecuteSqlRawAsync(InsertCountsSql, [releaseNumber], cancellationToken);
        if (!await IsExactAsync(db, releaseNumber, cancellationToken))
            throw new BillCountRefusedException($"Release {releaseNumber} party line-up snapshot differs from its published revisions.");
        return await db.Database.SqlQueryRaw<bool>(ExpectedAnyIssueSql).SingleAsync(cancellationToken);
    }

    /// <summary>Compares the saved rows of a release with the expected rows, in both directions, in one read-only statement.</summary>
    public static async Task<BillCountStatus> VerifyAsync(CollectionAttemptDbContext db, int releaseNumber, int? congress,
        CancellationToken cancellationToken)
    {
        if (congress is not { } selectedCongress)
            return await ExistsAsync(db, releaseNumber, cancellationToken) ? BillCountStatus.DifferentRows : BillCountStatus.Verified;
        var status = await db.Database.SqlQueryRaw<int>(VerifyStatusSql, releaseNumber, selectedCongress).SingleAsync(cancellationToken);
        return status switch { 0 => BillCountStatus.Verified, 1 => BillCountStatus.MissingRows, _ => BillCountStatus.DifferentRows };
    }

    private static async Task PrepareExpectedAsync(CollectionAttemptDbContext db, int releaseNumber, int congress, CancellationToken cancellationToken)
    {
        await ReleaseIssueCounts.EnsureEligibleBillsAsync(db, releaseNumber, congress, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(ExpectedSql, [releaseNumber, congress], cancellationToken);
    }

    private static Task<bool> ExistsAsync(CollectionAttemptDbContext db, int releaseNumber, CancellationToken cancellationToken) =>
        db.Database.SqlQueryRaw<bool>(ExistingSql, releaseNumber).SingleAsync(cancellationToken);

    private static Task<bool> IsExactAsync(CollectionAttemptDbContext db, int releaseNumber, CancellationToken cancellationToken) =>
        db.Database.SqlQueryRaw<bool>(VerifySql, releaseNumber).SingleAsync(cancellationToken);
}
