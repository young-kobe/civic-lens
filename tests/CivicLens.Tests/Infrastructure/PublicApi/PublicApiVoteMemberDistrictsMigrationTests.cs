using CivicLens.Infrastructure.Collection.Migrations;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Officials;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiVoteMemberDistrictsMigrationTests(PostgresCollection postgres)
{
    private static readonly SeededVote House = new('a', "2026-03-02");
    private static readonly SeededVote Senate = new('c', "2026-03-02") { Chamber = "Senate" };

    private async Task<string> SeedAsync(params SeededOfficial[] officials)
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        await ViewsSeed.ActivateReleaseAsync(database, 1, DateTimeOffset.UnixEpoch);
        await ViewsSeed.AddVotesAsync(database, House, Senate);
        await OfficialsSeed.AddOfficialsAsync(database, officials);
        return database;
    }

    [Fact]
    public async Task TheViewShowsTheTermStateAndDistrictOfAHouseMemberASenatorAnAtLargeMemberAndNullsForAMemberWithNoTerm()
    {
        var database = await SeedAsync(
            new SeededOfficial("p0", "Ann Able", new SeededTerm("Representative", "Representative", "2025-01-03") { State = "CA", District = 12 }),
            new SeededOfficial("p1", "Ben Baker", new SeededTerm("Representative", "Representative", "2025-01-03") { State = "AK", District = 0 }),
            new SeededOfficial("p2", "Cy Cole", new SeededTerm("Senator", "Senator", "2025-01-03") { State = "TX" }));
        try
        {
            Assert.Equal(["p0|CA|12", "p1|AK|0", "p2||"], await MembersAsync(database, House));
            Assert.Equal(["p0||", "p1||", "p2|TX|"], await MembersAsync(database, Senate));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task TheViewReadsTheTermOfTheChamberOfTheVoteAndTheNewestTermThatCoversTheVoteDay()
    {
        var database = await SeedAsync(
            new SeededOfficial("p0", "Ann Able",
                new SeededTerm("Representative", "Representative", "2019-01-03") { State = "NJ", District = 3, EndsOn = "2024-12-31" },
                new SeededTerm("Representative", "Representative", "2025-01-03") { State = "NJ", District = 5 },
                new SeededTerm("Senator", "Senator", "2025-01-03") { State = "NY" }),
            new SeededOfficial("p1", "Ben Baker", new SeededTerm("Representative", "Representative", "2026-04-01") { State = "OH", District = 1 }));
        try
        {
            Assert.Equal(["p0|NJ|5", "p1||", "p2||"], await MembersAsync(database, House));
            Assert.Equal(["p0|NY|", "p1||", "p2||"], await MembersAsync(database, Senate));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task TheDowngradeRemovesTheTwoColumnsAndKeepsTheSourcesViewThatReadsTheMembersView()
    {
        var database = await SeedAsync(new SeededOfficial("p0", "Ann Able", new SeededTerm("Representative", "Representative", "2025-01-03") { State = "CA", District = 12 }));
        try
        {
            var migration = new PublicApiVoteMemberDistricts();
            await ApplyAsync(database, migration.DownOperations);
            Assert.Equal(["record_id", "revision_number", "position", "official_id", "vote_cast", "party", "caucus", "extraction_id", "citation_start", "citation_length"],
                await QueryAsync(database, "SELECT attname FROM pg_attribute WHERE attrelid = 'public_api.vote_members'::regclass AND attnum > 0 ORDER BY attnum"));
            Assert.Equal(["true"], await QueryAsync(database, "SELECT (to_regclass('public_api.vote_sources') IS NOT NULL)::text"));
            Assert.Equal(["true|true"], await QueryAsync(database, """
                SELECT has_table_privilege('civic_lens_public', 'public_api.vote_members', 'SELECT')::text || '|'
                    || has_table_privilege('civic_lens_public', 'public_api.vote_sources', 'SELECT')::text
                """));

            await ApplyAsync(database, migration.UpOperations);
            Assert.Equal(["p0|CA|12", "p1||", "p2||"], await MembersAsync(database, House));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private static Task<string[]> MembersAsync(string database, SeededVote vote) => QueryAsync(database, $"""
        SELECT official_id || '|' || coalesce(state, '') || '|' || coalesce(district::text, '')
          FROM public_api.vote_members WHERE record_id = '{vote.RecordId}' ORDER BY "position"
        """);

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

    private static async Task<string[]> QueryAsync(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        return [.. rows];
    }
}
