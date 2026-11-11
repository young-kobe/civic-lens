using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Actions;
using CivicLens.Infrastructure.Collection;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace CivicLens.Infrastructure.Publication;

/// <summary>Owns the expected bill counts of one immutable release. The writer saves them from temporary tables, and the read-only verifier reads the same text as common table expressions.</summary>
internal static class ReleaseBillCounts
{
    private const string SelectedBody = """
        SELECT kind, record_id, revision_number
          FROM publication_record_states
         WHERE first_release <= {0} AND (last_release IS NULL OR last_release >= {0})
        """;

    private const string EligibleBillsBody = """
        SELECT a.record_id, a.revision_number, a.congress
          FROM selected s
          JOIN action_revisions a ON a.record_id = s.record_id AND a.revision_number = s.revision_number
         WHERE s.kind = 'action' AND a.headline IS NOT NULL AND a.event_on IS NOT NULL
           AND EXISTS (SELECT 1 FROM action_revision_sponsorships p
                        WHERE p.record_id = a.record_id AND p.revision_number = a.revision_number)
        """;

    private const string OfficialCountsBody = """
        SELECT eligible.congress, p.person_id AS official_id,
               count(DISTINCT eligible.record_id) FILTER (WHERE p.role = 'Sponsor')::integer AS sponsored,
               count(DISTINCT eligible.record_id) FILTER (WHERE p.role = 'Cosponsor' AND p.withdrawn_on IS NULL
                   AND NOT EXISTS (
                       SELECT 1 FROM action_revision_sponsorships sponsor
                        WHERE sponsor.record_id = p.record_id AND sponsor.revision_number = p.revision_number
                          AND sponsor.person_id = p.person_id AND sponsor.role = 'Sponsor'))::integer AS cosponsored
          FROM expected_release_eligible_bills eligible
          JOIN action_revision_sponsorships p ON p.record_id = eligible.record_id AND p.revision_number = eligible.revision_number
          JOIN release_officials officials ON officials.release_number = {0} AND officials.official_id = p.person_id
         GROUP BY eligible.congress, p.person_id
        HAVING count(DISTINCT eligible.record_id) FILTER (WHERE p.role = 'Sponsor') > 0
            OR count(DISTINCT eligible.record_id) FILTER (WHERE p.role = 'Cosponsor' AND p.withdrawn_on IS NULL
                AND NOT EXISTS (
                    SELECT 1 FROM action_revision_sponsorships sponsor
                     WHERE sponsor.record_id = p.record_id AND sponsor.revision_number = p.revision_number
                       AND sponsor.person_id = p.person_id AND sponsor.role = 'Sponsor')) > 0
        """;

    private const string CoverageBody = """
        SELECT {1}::integer AS congress WHERE {1}::integer IS NOT NULL
        UNION
        SELECT v.congress
          FROM selected s
          JOIN vote_revisions v ON v.record_id = s.record_id AND v.revision_number = s.revision_number
         WHERE s.kind = 'vote'
        UNION
        SELECT congress FROM expected_release_eligible_bills
        """;

    private const string BillTotalsBody = """
        SELECT congress, count(*)::integer AS bills FROM expected_release_eligible_bills GROUP BY congress
        """;

    private const string TotalsBody = """
        SELECT coverage.congress, coalesce(bill_totals.bills, 0) AS bills,
               (SELECT count(*)::integer FROM expected_release_official_bill_counts members
                 WHERE members.congress = coverage.congress) AS expected_member_row_count
          FROM coverage
          LEFT JOIN bill_totals USING (congress)
        """;

    private const string ExpectedMembersSql = $$"""
        DROP TABLE IF EXISTS pg_temp.expected_release_official_bill_counts;
        DROP TABLE IF EXISTS pg_temp.expected_release_bill_counts;
        DROP TABLE IF EXISTS pg_temp.expected_release_eligible_bills;
        CREATE TEMP TABLE expected_release_eligible_bills ON COMMIT DROP AS
        WITH selected AS ({{SelectedBody}})
        {{EligibleBillsBody}};

        CREATE TEMP TABLE expected_release_official_bill_counts ON COMMIT DROP AS
        {{OfficialCountsBody}};
        """;

    private const string ExpectedTotalsSql = $$"""
        CREATE TEMP TABLE expected_release_bill_counts ON COMMIT DROP AS
        WITH selected AS ({{SelectedBody}}), coverage AS ({{CoverageBody}}), bill_totals AS ({{BillTotalsBody}})
        {{TotalsBody}};
        """;

    private const string ExpectedCtes = $$"""
        WITH selected AS MATERIALIZED ({{SelectedBody}}),
             expected_release_eligible_bills AS MATERIALIZED ({{EligibleBillsBody}}),
             expected_release_official_bill_counts AS MATERIALIZED ({{OfficialCountsBody}}),
             coverage AS ({{CoverageBody}}),
             bill_totals AS ({{BillTotalsBody}}),
             expected_release_bill_counts AS ({{TotalsBody}})
        """;

    private const string InsertTotalsSql = """
        INSERT INTO release_bill_counts (release_number, congress, bills, expected_member_row_count)
        SELECT {0}, congress, bills, expected_member_row_count FROM expected_release_bill_counts
        ON CONFLICT (release_number, congress) DO NOTHING
        """;

    private const string InsertMembersSql = """
        INSERT INTO release_official_bill_counts (release_number, congress, official_id, sponsored, cosponsored)
        SELECT {0}, congress, official_id, sponsored, cosponsored FROM expected_release_official_bill_counts
        ON CONFLICT (release_number, congress, official_id) DO NOTHING
        """;

    private const string ExactMatchPredicate = """
        (NOT EXISTS (
            (SELECT congress, bills, expected_member_row_count FROM expected_release_bill_counts
             EXCEPT SELECT congress, bills, expected_member_row_count FROM release_bill_counts WHERE release_number = {0})
            UNION ALL
            (SELECT congress, bills, expected_member_row_count FROM release_bill_counts WHERE release_number = {0}
             EXCEPT SELECT congress, bills, expected_member_row_count FROM expected_release_bill_counts)
        ) AND NOT EXISTS (
            (SELECT congress, official_id, sponsored, cosponsored FROM expected_release_official_bill_counts
             EXCEPT SELECT congress, official_id, sponsored, cosponsored FROM release_official_bill_counts WHERE release_number = {0})
            UNION ALL
            (SELECT congress, official_id, sponsored, cosponsored FROM release_official_bill_counts WHERE release_number = {0}
             EXCEPT SELECT congress, official_id, sponsored, cosponsored FROM expected_release_official_bill_counts)
        ))
        """;

    private const string VerifySql = $$"""
        SELECT {{ExactMatchPredicate}} AS "Value"
        """;

    private const string VerifyReadOnlySql = $$"""
        {{ExpectedCtes}}
        SELECT {{ExactMatchPredicate}} AS "Matches",
               (EXISTS (SELECT 1 FROM release_bill_counts WHERE release_number = {0})
                OR EXISTS (SELECT 1 FROM release_official_bill_counts WHERE release_number = {0})) AS "HasRows"
        """;

    private const string ExistingSql = """
        SELECT EXISTS (SELECT 1 FROM release_bill_counts WHERE release_number = {0})
            OR EXISTS (SELECT 1 FROM release_official_bill_counts WHERE release_number = {0}) AS "Value"
        """;

    private const string ActionBindingsSql = """
        SELECT count(*)::integer AS "Value"
          FROM unnest({0}, {1}) AS requested(id, number)
          JOIN action_revisions a ON a.record_id = requested.id AND a.revision_number = requested.number
        """;

    private const string VoteBindingsSql = """
        SELECT count(*)::integer AS "Value"
          FROM unnest({0}, {1}) AS requested(id, number)
          JOIN vote_revisions v ON v.record_id = requested.id AND v.revision_number = requested.number
        """;

    public static async Task<bool> FillAsync(CollectionAttemptDbContext db, int releaseNumber, int? releaseCongress,
        IReadOnlyCollection<PublishedRevisionBinding> bindings,
        CancellationToken cancellationToken)
    {
        var requested = bindings.Distinct().ToArray();
        var actions = requested.Where(binding => binding.Kind == PublishedRecordKind.Action).ToArray();
        var votes = requested.Where(binding => binding.Kind == PublishedRecordKind.Vote).ToArray();
        await RequireStoredAsync(db, ActionBindingsSql, actions, releaseNumber, "action", cancellationToken);
        await RequireStoredAsync(db, VoteBindingsSql, votes, releaseNumber, "vote", cancellationToken);

        await db.Database.ExecuteSqlRawAsync(ExpectedMembersSql, [releaseNumber], cancellationToken);
        await db.Database.ExecuteSqlRawAsync(ExpectedTotalsSql, [releaseNumber, CongressParameter(releaseCongress)], cancellationToken);
        var existing = await db.Database.SqlQueryRaw<bool>(ExistingSql, releaseNumber).SingleAsync(cancellationToken);
        if (existing)
        {
            var exactReplay = await db.Database.SqlQueryRaw<bool>(VerifySql, releaseNumber).SingleAsync(cancellationToken);
            if (!exactReplay)
                throw new BillCountRefusedException($"Release {releaseNumber} already has an incomplete or changed bill count snapshot.");
            return false;
        }
        await db.Database.ExecuteSqlRawAsync(InsertTotalsSql, [releaseNumber], cancellationToken);
        await db.Database.ExecuteSqlRawAsync(InsertMembersSql, [releaseNumber], cancellationToken);
        var exact = await db.Database.SqlQueryRaw<bool>(VerifySql, releaseNumber).SingleAsync(cancellationToken);
        if (!exact)
            throw new BillCountRefusedException($"Release {releaseNumber} bill count snapshots differ from its published revisions.");
        return true;
    }

    /// <summary>Compares the saved rows with the expected counts in both directions. It writes nothing, so it runs in a read-only transaction.</summary>
    public static async Task<BillCountStatus> VerifyAsync(CollectionAttemptDbContext db, int releaseNumber, int? releaseCongress,
        CancellationToken cancellationToken)
    {
        var row = await db.Database.SqlQueryRaw<VerificationRow>(VerifyReadOnlySql, releaseNumber, CongressParameter(releaseCongress))
            .SingleAsync(cancellationToken);
        if (!row.HasRows) return BillCountStatus.MissingRows;
        return row.Matches ? BillCountStatus.Verified : BillCountStatus.DifferentRows;
    }

    private static async Task RequireStoredAsync(CollectionAttemptDbContext db, string sql, PublishedRevisionBinding[] bindings,
        int releaseNumber, string kind, CancellationToken cancellationToken)
    {
        var stored = await db.Database.SqlQueryRaw<int>(sql,
            [bindings.Select(binding => binding.RecordId).ToArray(), bindings.Select(binding => binding.RevisionNumber).ToArray()]).SingleAsync(cancellationToken);
        if (stored != bindings.Length)
            throw new BillCountRefusedException($"Release {releaseNumber} contains a selected {kind} revision that is not stored.");
    }

    private static NpgsqlParameter CongressParameter(int? congress) =>
        new("congress", NpgsqlDbType.Integer) { Value = (object?)congress ?? DBNull.Value };

    private sealed class VerificationRow
    {
        public bool Matches { get; set; }
        public bool HasRows { get; set; }
    }
}
