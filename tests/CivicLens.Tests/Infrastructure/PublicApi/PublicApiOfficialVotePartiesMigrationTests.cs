using CivicLens.Infrastructure.Collection.Migrations;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using static CivicLens.Tests.PublicApi.Votes.ViewsSeed;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiOfficialVotePartiesMigrationTests(PostgresCollection postgres)
{
    private const string VotePartiesSql = """
        SELECT concat_ws('|', official_id, chamber, congress, coalesce(party, '-'), held, missed, first_voted_on, last_voted_on,
                         with_party, against_party, cast_count, coalesce(first_cast_on::text, '-'), coalesce(last_cast_on::text, '-'), unwritten_votes)
          FROM public_api.official_vote_parties ORDER BY official_id, chamber, party
        """;

    private const string AttendanceSql = """
        SELECT concat_ws('|', official_id, chamber, congress, held, missed, first_voted_on, last_voted_on)
          FROM public_api.official_attendance ORDER BY official_id, chamber
        """;

    private const string PartyLinesSql = """
        SELECT concat_ws('|', official_id, chamber, congress, party, with_party, against_party, cast_count, first_voted_on, last_voted_on)
          FROM public_api.official_party_lines ORDER BY official_id, chamber, party
        """;

    // On the split votes a and b most Democrats vote Yea and most Republicans vote Nay. Vote c is not split. Vote d has no stored alignment.
    // p-switch is a Democrat on vote a and a Republican on votes b and c, so it has two party rows that share one attendance.
    private static readonly SeededVote[] Votes =
    [
        new('a', "2025-03-01") { RollCall = 1, PersonIds = ["p-switch", "p-loyal", "p-break", "p-absent"], Casts = ["Yea", "Nay", "Yea", "Not Voting"] },
        new('b', "2025-04-01") { RollCall = 2, PersonIds = ["p-switch", "p-loyal", "p-break", "p-absent"], Casts = ["Nay", "Nay", "Yea", "Yea"] },
        new('c', "2025-05-01") { RollCall = 3, PersonIds = ["p-switch", "p-other", "p-null"], Casts = ["Yea", "Yea", "Yea"] },
        new('d', "2025-06-01") { RollCall = 1, Chamber = "Senate", PersonIds = ["s-one"], Casts = ["Yea"] }
    ];

    private static readonly string[] ExpectedVoteParties =
    [
        "p-absent|House|119|Democrat|2|1|2025-03-01|2025-04-01|1|0|1|2025-04-01|2025-04-01|0",
        "p-break|House|119|Republican|2|0|2025-03-01|2025-04-01|0|2|2|2025-03-01|2025-04-01|0",
        "p-loyal|House|119|Republican|2|0|2025-03-01|2025-04-01|2|0|2|2025-03-01|2025-04-01|0",
        "p-null|House|119|-|1|0|2025-05-01|2025-05-01|0|0|0|-|-|0",
        "p-other|House|119|Independent|1|0|2025-05-01|2025-05-01|0|0|0|-|-|0",
        "p-switch|House|119|Democrat|3|0|2025-03-01|2025-05-01|1|0|1|2025-03-01|2025-03-01|0",
        "p-switch|House|119|Republican|3|0|2025-03-01|2025-05-01|1|0|1|2025-04-01|2025-04-01|0",
        "s-one|Senate|119|Democrat|1|0|2025-06-01|2025-06-01|0|0|0|-|-|1"
    ];

    private static readonly string[] ExpectedAttendance =
    [
        "p-absent|House|119|2|1|2025-03-01|2025-04-01",
        "p-break|House|119|2|0|2025-03-01|2025-04-01",
        "p-loyal|House|119|2|0|2025-03-01|2025-04-01",
        "p-null|House|119|1|0|2025-05-01|2025-05-01",
        "p-other|House|119|1|0|2025-05-01|2025-05-01",
        "p-switch|House|119|3|0|2025-03-01|2025-05-01",
        "s-one|Senate|119|1|0|2025-06-01|2025-06-01"
    ];

    private static readonly string[] ExpectedPartyLines =
    [
        "p-absent|House|119|Democrat|1|0|1|2025-04-01|2025-04-01",
        "p-break|House|119|Republican|0|2|2|2025-03-01|2025-04-01",
        "p-loyal|House|119|Republican|2|0|2|2025-03-01|2025-04-01",
        "p-switch|House|119|Democrat|1|0|1|2025-03-01|2025-03-01",
        "p-switch|House|119|Republican|1|0|1|2025-04-01|2025-04-01"
    ];

    [Fact]
    public async Task OneRowPerOfficialChamberCongressAndPartyGivesTheSameAttendanceAndPartyLinesAsTheOldViewsAndTheRollbackKeepsThem()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await ViewsSeed.ActivateReleaseAsync(database, 1, DateTimeOffset.UnixEpoch, recordCount: Votes.Length);
            await ViewsSeed.AddVotesAsync(database, Votes);
            await ViewsSeed.ExecuteAsync(database, $"""
                SET session_replication_role = replica;
                UPDATE vote_revisions SET columns_written = true, alignment = CASE record_id WHEN '{Votes[2].RecordId}' THEN 'PartiesAgree' ELSE 'Split' END,
                       democrat_majority = 'Yea', republican_majority = CASE record_id WHEN '{Votes[2].RecordId}' THEN 'Yea' ELSE 'Nay' END
                 WHERE record_id <> '{Votes[3].RecordId}';
                UPDATE vote_member_list_entries e SET party = CASE
                       WHEN e.person_id = 'p-switch' AND v.record_id = '{Votes[0].RecordId}' THEN 'Democrat'
                       WHEN e.person_id IN ('p-switch', 'p-loyal', 'p-break') THEN 'Republican'
                       WHEN e.person_id = 'p-other' THEN 'Independent'
                       WHEN e.person_id = 'p-null' THEN NULL
                       ELSE 'Democrat' END
                  FROM vote_revisions v WHERE v.member_list_id = e.list_id;
                """);

            Assert.Equal(ExpectedVoteParties, await QueryAsPublicAsync(database, VotePartiesSql));
            Assert.Equal(ExpectedAttendance, await QueryAsPublicAsync(database, AttendanceSql));
            Assert.Equal(ExpectedPartyLines, await QueryAsPublicAsync(database, PartyLinesSql));

            var migration = new PublicApiOfficialVoteParties();
            await ApplyAsync(database, migration.DownOperations);
            Assert.Empty(await QueryAsync(database, "SELECT 1 FROM pg_class WHERE oid = to_regclass('public_api.official_vote_parties')"));
            Assert.Equal(ExpectedAttendance, await QueryAsPublicAsync(database, AttendanceSql));
            Assert.Equal(ExpectedPartyLines, await QueryAsPublicAsync(database, PartyLinesSql));

            await ApplyAsync(database, migration.UpOperations);
            await ViewsSeed.ExecuteAsync(database, "GRANT SELECT ON public_api.official_vote_parties TO civic_lens_public");
            Assert.Equal(ExpectedVoteParties, await QueryAsPublicAsync(database, VotePartiesSql));
            Assert.Equal(ExpectedAttendance, await QueryAsPublicAsync(database, AttendanceSql));
            Assert.Equal(ExpectedPartyLines, await QueryAsPublicAsync(database, PartyLinesSql));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private static async Task ApplyAsync(string database, IReadOnlyList<MigrationOperation> operations)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (var operation in operations)
        {
            await using var command = new NpgsqlCommand(Assert.IsType<SqlOperation>(operation).Sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }
}
