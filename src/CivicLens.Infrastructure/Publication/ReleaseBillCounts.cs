using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Actions;
using CivicLens.Infrastructure.Collection;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Publication;

/// <summary>Writes the bill counts that belong to one immutable release.</summary>
internal static class ReleaseBillCounts
{
    private const int ValidationBatchSize = 100;

    private const string ExpectedMembersSql = """
        DROP TABLE IF EXISTS pg_temp.expected_release_official_bill_counts;
        DROP TABLE IF EXISTS pg_temp.expected_release_bill_counts;
        DROP TABLE IF EXISTS pg_temp.expected_release_eligible_bills;
        CREATE TEMP TABLE expected_release_eligible_bills ON COMMIT DROP AS
        WITH selected AS (
            SELECT kind, record_id, revision_number
              FROM publication_record_states
             WHERE first_release <= {0} AND (last_release IS NULL OR last_release >= {0})
        )
        SELECT a.record_id, a.revision_number, a.congress
          FROM selected s
          JOIN action_revisions a ON a.record_id = s.record_id AND a.revision_number = s.revision_number
         WHERE s.kind = 'action' AND a.headline IS NOT NULL AND a.event_on IS NOT NULL
           AND EXISTS (SELECT 1 FROM action_revision_sponsorships p
                        WHERE p.record_id = a.record_id AND p.revision_number = a.revision_number);

        CREATE TEMP TABLE expected_release_official_bill_counts ON COMMIT DROP AS
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
                       AND sponsor.person_id = p.person_id AND sponsor.role = 'Sponsor')) > 0;
        """;

    private const string ExpectedTotalsSql = """
        CREATE TEMP TABLE expected_release_bill_counts ON COMMIT DROP AS
        WITH selected AS (
            SELECT kind, record_id, revision_number
              FROM publication_record_states
             WHERE first_release <= {0} AND (last_release IS NULL OR last_release >= {0})
        ), coverage AS (
            SELECT {1}::integer AS congress WHERE {1} IS NOT NULL
            UNION
            SELECT v.congress
              FROM selected s
              JOIN vote_revisions v ON v.record_id = s.record_id AND v.revision_number = s.revision_number
             WHERE s.kind = 'vote'
            UNION
            SELECT congress FROM expected_release_eligible_bills
        ), bill_totals AS (
            SELECT congress, count(*)::integer AS bills FROM expected_release_eligible_bills GROUP BY congress
        )
        SELECT coverage.congress, coalesce(bill_totals.bills, 0) AS bills,
               (SELECT count(*)::integer FROM expected_release_official_bill_counts members
                 WHERE members.congress = coverage.congress) AS expected_member_row_count
          FROM coverage
          LEFT JOIN bill_totals USING (congress);
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

    private const string VerifySql = """
        SELECT (NOT EXISTS (
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
        )) AS "Value"
        """;

    private const string ExistingSql = """
        SELECT EXISTS (SELECT 1 FROM release_bill_counts WHERE release_number = {0})
            OR EXISTS (SELECT 1 FROM release_official_bill_counts WHERE release_number = {0}) AS "Value"
        """;

    private const string VoteBindingsSql = """
        SELECT count(*)::integer AS "Value"
          FROM unnest({0}, {1}) AS requested(id, number)
          JOIN vote_revisions v ON v.record_id = requested.id AND v.revision_number = requested.number
        """;

    public static async Task FillAsync(CollectionAttemptDbContext db, int releaseNumber, int? releaseCongress,
        IReadOnlyCollection<PublishedRevisionBinding> bindings,
        CancellationToken cancellationToken)
    {
        var requested = bindings.Distinct().ToArray();
        var actions = requested.Where(binding => binding.Kind == PublishedRecordKind.Action).ToArray();
        var votes = requested.Where(binding => binding.Kind == PublishedRecordKind.Vote).ToArray();
        foreach (var batch in actions.Chunk(ValidationBatchSize))
            await ActionRevisionRows.ValidateBillCountBindingsAsync(db, batch, cancellationToken);
        var voteCount = await db.Database.SqlQueryRaw<int>(VoteBindingsSql,
            [votes.Select(binding => binding.RecordId).ToArray(), votes.Select(binding => binding.RevisionNumber).ToArray()]).SingleAsync(cancellationToken);
        if (voteCount != votes.Length)
            throw new InvalidOperationException($"Release {releaseNumber} contains a selected vote revision that is not stored.");

        await db.Database.ExecuteSqlRawAsync(ExpectedMembersSql, [releaseNumber], cancellationToken);
        await db.Database.ExecuteSqlRawAsync(ExpectedTotalsSql, [releaseNumber, (object?)releaseCongress ?? DBNull.Value], cancellationToken);
        var existing = await db.Database.SqlQueryRaw<bool>(ExistingSql, releaseNumber).SingleAsync(cancellationToken);
        if (existing)
        {
            var exactReplay = await db.Database.SqlQueryRaw<bool>(VerifySql, releaseNumber).SingleAsync(cancellationToken);
            if (!exactReplay)
                throw new InvalidOperationException($"Release {releaseNumber} already has an incomplete or changed bill count snapshot.");
            return;
        }
        await db.Database.ExecuteSqlRawAsync(InsertTotalsSql, [releaseNumber], cancellationToken);
        await db.Database.ExecuteSqlRawAsync(InsertMembersSql, [releaseNumber], cancellationToken);
        var exact = await db.Database.SqlQueryRaw<bool>(VerifySql, releaseNumber).SingleAsync(cancellationToken);
        if (!exact)
            throw new InvalidOperationException($"Release {releaseNumber} bill count snapshots differ from its published revisions.");
    }
}
