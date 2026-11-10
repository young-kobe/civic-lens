using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiOfficialPartyLinesMigrationTests(PostgresCollection postgres)
{
    private const string Migration = "20261107210000_PublicApiOfficialPartyLines";

    [Fact]
    public async Task UpgradeAppendsPinnedColumnsAndDowngradeRestoresMemberVoteAccess()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, Migration) < 0);
            await db.GetService<IMigrator>().MigrateAsync(previous);
            var before = await ColumnsAsync(connectionString);
            Assert.DoesNotContain("party", before);
            Assert.DoesNotContain("alignment", before);
            var store = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
            await store.MigrateAsync();

            var after = await ColumnsAsync(connectionString);
            Assert.Equal(before, after[..before.Length]);
            Assert.Equal(["party", "alignment"], after[before.Length..(before.Length + 2)]);
            var partyLineDefinition = await QueryAsync(connectionString, "SELECT pg_get_viewdef('public_api.official_party_lines'::regclass)");
            Assert.Contains("FROM public_api.member_votes", partyLineDefinition, StringComparison.Ordinal);
            Assert.DoesNotContain("public_api.votes", partyLineDefinition, StringComparison.Ordinal);
            Assert.DoesNotContain("public_api.vote_members", partyLineDefinition, StringComparison.Ordinal);

            await ReadPartyLinesAsync(connectionString);
            await using (var owner = new NpgsqlConnection(connectionString))
            {
                await owner.OpenAsync();
                await using var revoke = new NpgsqlCommand("REVOKE SELECT ON public_api.official_party_lines FROM civic_lens_public", owner);
                await revoke.ExecuteNonQueryAsync();
            }
            var denied = await Assert.ThrowsAsync<PostgresException>(() => ReadPartyLinesAsync(connectionString));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
            await store.MigrateAsync();
            await ReadPartyLinesAsync(connectionString);

            await db.GetService<IMigrator>().MigrateAsync(previous);
            Assert.Equal(before, await ColumnsAsync(connectionString));
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var publicRead = new NpgsqlCommand("SET ROLE civic_lens_public; SELECT official_id FROM public_api.member_votes LIMIT 0;", connection);
            await publicRead.ExecuteNonQueryAsync();
        }
        finally { await postgres.DropDatabaseAsync(connectionString); }
    }

    private static async Task ReadPartyLinesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var read = new NpgsqlCommand("SET ROLE civic_lens_public; SELECT official_id FROM public_api.official_party_lines LIMIT 0;", connection);
        await read.ExecuteNonQueryAsync();
    }

    private static async Task<string[]> ColumnsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT column_name FROM information_schema.columns
             WHERE table_schema = 'public_api' AND table_name = 'member_votes'
             ORDER BY ordinal_position
            """, connection);
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
        return [.. columns];
    }

    private static async Task<string> QueryAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
