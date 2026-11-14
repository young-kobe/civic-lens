using CivicLens.Infrastructure.Collection.Migrations;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using static CivicLens.Tests.PublicApi.Votes.ViewsSeed;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiMemberVoteBreaksPartyMigrationTests(PostgresCollection postgres)
{
    private const string BreaksSql = "SELECT official_id || '|' || coalesce(breaks_party::text, 'none') FROM public_api.member_votes ORDER BY official_id";
    private const string PartyLinesSql = "SELECT official_id || '|' || with_party || '|' || against_party || '|' || cast_count FROM public_api.official_party_lines ORDER BY official_id";

    // Each vote has one member. Only a Split vote with a Democrat or Republican who cast Yea or Nay and a party majority has a party line.
    private static readonly SeededVote[] Votes =
    [
        new('a', "2025-03-01") { Casts = ["Nay"], PersonIds = ["m-break"] },
        new('b', "2025-03-02") { Casts = ["Nay"], PersonIds = ["m-with"] },
        new('c', "2025-03-03") { Casts = ["Nay"], PersonIds = ["m-independent"] },
        new('d', "2025-03-04") { Casts = ["Nay"], PersonIds = ["m-tie"] },
        new('e', "2025-03-05") { Casts = ["Not Voting"], PersonIds = ["m-absent"] },
        new('f', "2025-03-06") { Casts = ["Nay"], PersonIds = ["m-agree"] },
        new('9', "2025-03-07") { Casts = ["Aye"], PersonIds = ["m-aye"] }
    ];

    private static readonly string[] ExpectedBreaks =
        ["m-absent|none", "m-agree|none", "m-aye|false", "m-break|true", "m-independent|none", "m-tie|none", "m-with|false"];

    private static readonly string[] ExpectedPartyLines = ["m-aye|1|0|1", "m-break|0|1|1", "m-with|1|0|1"];

    [Fact]
    public async Task OneColumnNamesTheVotesThatBrokeFromThePartyAndThePartyLinesCountExactlyThoseVotesAndTheRollbackRestoresTheOldViews()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await ApplyAsync(database, new PublicApiMemberVoteTextOrigin().DownOperations);
            await ViewsSeed.ActivateReleaseAsync(database, 1, DateTimeOffset.UnixEpoch, recordCount: Votes.Length);
            await ViewsSeed.AddVotesAsync(database, Votes);
            await ViewsSeed.ExecuteAsync(database, """
                SET session_replication_role = replica;
                UPDATE vote_revisions
                   SET columns_written = true, alignment = CASE WHEN left(record_id, 1) = 'f' THEN 'PartiesAgree' ELSE 'Split' END,
                       democrat_majority = CASE WHEN left(record_id, 1) = 'd' THEN NULL ELSE 'Yea' END, republican_majority = 'Nay';
                UPDATE vote_member_list_entries
                   SET party = CASE person_id WHEN 'm-with' THEN 'Republican' WHEN 'm-independent' THEN 'Independent' ELSE 'Democrat' END;
                """);

            Assert.Equal(ExpectedBreaks, await QueryAsPublicAsync(database, BreaksSql));
            Assert.Equal(ExpectedPartyLines, await QueryAsPublicAsync(database, PartyLinesSql));

            var migration = new PublicApiMemberVoteBreaksParty();
            var columns = await ColumnNamesAsync(database);
            await ApplyAsync(database, migration.DownOperations);
            Assert.Equal(columns[..^1], await ColumnNamesAsync(database));
            Assert.Equal(ExpectedPartyLines, await QueryAsPublicAsync(database, PartyLinesSql));
            await ApplyAsync(database, migration.UpOperations);
            Assert.Equal(columns, await ColumnNamesAsync(database));
            Assert.Equal(ExpectedBreaks, await QueryAsPublicAsync(database, BreaksSql));
            Assert.Equal(ExpectedPartyLines, await QueryAsPublicAsync(database, PartyLinesSql));
            Assert.Equal("breaks_party", columns[^1]);
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

    private static Task<string[]> ColumnNamesAsync(string database) => QueryAsync(database,
        "SELECT column_name FROM information_schema.columns WHERE table_schema = 'public_api' AND table_name = 'member_votes' ORDER BY ordinal_position");

}
