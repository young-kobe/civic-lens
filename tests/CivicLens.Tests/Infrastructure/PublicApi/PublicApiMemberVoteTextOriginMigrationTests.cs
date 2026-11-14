using CivicLens.Infrastructure.Collection.Migrations;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using static CivicLens.Tests.PublicApi.Votes.ViewsSeed;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiMemberVoteTextOriginMigrationTests(PostgresCollection postgres)
{
    private const string OriginsSql = "SELECT official_id || '|' || text_origin || '|' || label FROM public_api.member_votes ORDER BY official_id";
    private const string PartyLinesSql = "SELECT official_id || '|' || with_party || '|' || against_party || '|' || cast_count FROM public_api.official_party_lines ORDER BY official_id";

    private static readonly SeededVote[] Votes =
    [
        new('a', "2025-03-01") { Casts = ["Nay"], PersonIds = ["m-code"], TextOrigin = "Code" },
        new('b', "2025-03-02") { Casts = ["Yea"], PersonIds = ["m-model"], TextOrigin = "Model" }
    ];

    private static readonly string[] ExpectedOrigins =
        ["m-code|Code|Written automatically from Congress.gov data, with no AI", "m-model|Model|An AI summary of the official record"];

    private static readonly string[] ExpectedPartyLines = ["m-code|0|1|1", "m-model|1|0|1"];

    [Fact]
    public async Task TheLastColumnNamesWhoWroteTheHeadlineAndTheRollbackDropsOnlyThatColumnAndKeepsThePartyLines()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await ViewsSeed.ActivateReleaseAsync(database, 1, DateTimeOffset.UnixEpoch, recordCount: Votes.Length);
            await ViewsSeed.AddVotesAsync(database, Votes);
            await ViewsSeed.ExecuteAsync(database, """
                SET session_replication_role = replica;
                UPDATE vote_revisions SET columns_written = true, alignment = 'Split', democrat_majority = 'Yea', republican_majority = 'Nay';
                UPDATE vote_member_list_entries SET party = 'Democrat';
                """);

            Assert.Equal(ExpectedOrigins, await QueryAsPublicAsync(database, OriginsSql));
            Assert.Equal(ExpectedPartyLines, await QueryAsPublicAsync(database, PartyLinesSql));

            await ApplyAsync(database, new PublicApiOfficialVoteParties().DownOperations);
            var migration = new PublicApiMemberVoteTextOrigin();
            var columns = await ColumnNamesAsync(database);
            Assert.Equal("text_origin", columns[^1]);
            await ApplyAsync(database, migration.DownOperations);
            Assert.Equal(columns[..^1], await ColumnNamesAsync(database));
            Assert.Equal(ExpectedPartyLines, await QueryAsPublicAsync(database, PartyLinesSql));
            await ApplyAsync(database, migration.UpOperations);
            Assert.Equal(columns, await ColumnNamesAsync(database));
            Assert.Equal(ExpectedOrigins, await QueryAsPublicAsync(database, OriginsSql));
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

    private static Task<string[]> ColumnNamesAsync(string database) => QueryAsync(database,
        "SELECT column_name FROM information_schema.columns WHERE table_schema = 'public_api' AND table_name = 'member_votes' ORDER BY ordinal_position");
}
