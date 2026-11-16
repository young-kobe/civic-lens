using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Publication;

/// <summary>
/// Owns the record counts of each official of one immutable release: every record that names the official, which the officials list shows,
/// and the votes, bills, statements, and executive actions tabs of the official record page. Each count repeats the filter of its public view
/// at the base tables, because the views show only the active release. The eligible bills come from <see cref="ReleaseBillCounts"/>, so the bills rule has one owner.
/// Every official of the release has one row, with zeros, so a missing row is a defect. The writer saves the rows once, and the read-only verifier compares them.
/// </summary>
internal static class ReleaseOfficialRecordCounts
{
    // The filters of public_api.officials (record_count), member_votes, bill_sponsorships, statements with statement_speakers, and executive_actions.
    // {0} is the release number and {1} the substantive vote kinds. The callers supply selected and expected_release_eligible_bills from ReleaseBillCounts.
    private const string ExpectedBody = """
        SELECT o.official_id, coalesce(records.n, 0) AS records, coalesce(votes.n, 0) AS votes, coalesce(bills.n, 0) AS bills,
               coalesce(statements.n, 0) AS statements, coalesce(executive.n, 0) AS executive_actions
          FROM release_officials o
          LEFT JOIN (SELECT e.official_id, count(*)::integer AS n
                       FROM selected s
                       JOIN published_entry_officials e ON e.kind = s.kind AND e.record_id = s.record_id AND e.revision_number = s.revision_number
                      GROUP BY e.official_id) records ON records.official_id = o.official_id
          LEFT JOIN (SELECT e.person_id AS official_id, count(*)::integer AS n
                       FROM selected s
                       JOIN vote_revisions v ON v.record_id = s.record_id AND v.revision_number = s.revision_number
                       JOIN vote_member_list_entries e ON e.list_id = v.member_list_id
                      WHERE s.kind = 'vote' AND v.kind = ANY({1})
                      GROUP BY e.person_id) votes ON votes.official_id = o.official_id
          LEFT JOIN (SELECT p.person_id AS official_id, count(*)::integer AS n
                       FROM expected_release_eligible_bills b
                       JOIN action_revision_sponsorships p ON p.record_id = b.record_id AND p.revision_number = b.revision_number
                      WHERE p.role = 'Sponsor'
                      GROUP BY p.person_id) bills ON bills.official_id = o.official_id
          LEFT JOIN (SELECT p.official_id, count(DISTINCT s.record_id)::integer AS n
                       FROM selected s
                       JOIN statement_revisions r ON r.candidate_id = s.record_id AND r.revision_number = s.revision_number
                       JOIN published_entries e ON e.kind = 'statement' AND e.record_id = s.record_id AND e.revision_number = s.revision_number
                       JOIN statement_revision_speakers p ON p.candidate_id = r.candidate_id AND p.revision_number = r.revision_number
                      WHERE s.kind = 'statement' AND r.headline IS NOT NULL AND r.summary IS NOT NULL AND e.approved_by IS NOT NULL
                      GROUP BY p.official_id) statements ON statements.official_id = o.official_id
          LEFT JOIN (SELECT r.person_id AS official_id, count(*)::integer AS n
                       FROM selected s
                       JOIN executive_action_revisions r ON r.record_id = s.record_id AND r.revision_number = s.revision_number
                      WHERE s.kind = 'executiveAction' AND r.title IS NOT NULL
                      GROUP BY r.person_id) executive ON executive.official_id = o.official_id
         WHERE o.release_number = {0}
        """;

    private const string Columns = "official_id, records, votes, bills, statements, executive_actions";

    private const string ExpectedCtes = $$"""
        WITH selected AS MATERIALIZED ({{ReleaseBillCounts.SelectedBody}}),
             expected_release_eligible_bills AS MATERIALIZED ({{ReleaseBillCounts.EligibleBillsBody}})
        """;

    private const string ExpectedSql = $$"""
        DROP TABLE IF EXISTS pg_temp.expected_release_official_record_counts;
        CREATE TEMP TABLE expected_release_official_record_counts ON COMMIT DROP AS
        {{ExpectedCtes}}
        {{ExpectedBody}};
        """;

    private const string ExactMatchPredicate = $"""
        NOT EXISTS (
            (SELECT {Columns} FROM expected_release_official_record_counts
             EXCEPT SELECT {Columns} FROM release_official_record_counts WHERE release_number = {"{0}"})
            UNION ALL
            (SELECT {Columns} FROM release_official_record_counts WHERE release_number = {"{0}"}
             EXCEPT SELECT {Columns} FROM expected_release_official_record_counts))
        """;

    private const string ExistingSql = """SELECT EXISTS (SELECT 1 FROM release_official_record_counts WHERE release_number = {0}) AS "Value" """;

    private const string InsertSql = $"""
        INSERT INTO release_official_record_counts (release_number, {Columns})
        SELECT {"{0}"}, {Columns} FROM expected_release_official_record_counts
        """;

    private const string VerifySql = $"""SELECT {ExactMatchPredicate} AS "Value" """;

    private const string VerifyReadOnlySql = $$"""
        {{ExpectedCtes}},
             expected_release_official_record_counts AS MATERIALIZED ({{ExpectedBody}})
        SELECT {{ExactMatchPredicate}} AS "Matches",
               EXISTS (SELECT 1 FROM release_official_record_counts WHERE release_number = {0}) AS "HasRows"
        """;

    /// <summary>Writes the rows of the release, or accepts rows that equal the expected counts. It returns false when it wrote no row.</summary>
    public static async Task<bool> FillAsync(CollectionAttemptDbContext db, int releaseNumber, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync(ExpectedSql, [releaseNumber, PublishedVoteKindExtensions.SubstantiveNames()], cancellationToken);
        var existing = await db.Database.SqlQueryRaw<bool>(ExistingSql, releaseNumber).SingleAsync(cancellationToken);
        var inserted = existing ? 0 : await db.Database.ExecuteSqlRawAsync(InsertSql, [releaseNumber], cancellationToken);
        if (!await db.Database.SqlQueryRaw<bool>(VerifySql, releaseNumber).SingleAsync(cancellationToken))
            throw new BillCountRefusedException(existing
                ? $"Release {releaseNumber} already has an incomplete or changed official record count snapshot."
                : $"Release {releaseNumber} official record count snapshots differ from its published revisions.");
        return inserted > 0;
    }

    /// <summary>Compares the saved rows with the expected counts in both directions. A release with no officials expects no rows.</summary>
    public static async Task<BillCountStatus> VerifyAsync(CollectionAttemptDbContext db, int releaseNumber, CancellationToken cancellationToken)
    {
        var row = await db.Database.SqlQueryRaw<VerificationRow>(VerifyReadOnlySql, releaseNumber, PublishedVoteKindExtensions.SubstantiveNames())
            .SingleAsync(cancellationToken);
        if (row.Matches) return BillCountStatus.Verified;
        return row.HasRows ? BillCountStatus.DifferentRows : BillCountStatus.MissingRows;
    }

    private sealed class VerificationRow
    {
        public bool Matches { get; set; }
        public bool HasRows { get; set; }
    }
}
