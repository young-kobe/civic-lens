using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Publication;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class OfficialSortNamesMigrationTests(PostgresCollection postgres)
{
    private const string Migration = "20261115010148_OfficialSortNames";

    [Fact]
    public async Task UpgradeAddsEmptySortNamesThatTheViewShowsAsTheNameAndDowngradeRestoresTheTablesAndTheView()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);
            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, Migration) < 0);
            await db.GetService<IMigrator>().MigrateAsync(previous);
            var beforeView = await ColumnsAsync(database, "public_api", "officials");
            string[] tables = ["release_officials", "roster_members", "executive_officials"];
            var beforeTables = await Task.WhenAll(tables.Select(table => ColumnsAsync(database, "public", table)));
            await ViewsSeed.ActivateReleaseAsync(database, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
            await ViewsSeed.ExecuteAsync(database, "INSERT INTO release_officials (release_number, official_id, name) VALUES (1, 'vh', 'Chris Van Hollen')");
            var store = PostgresCollectionAttemptStore.FromConnectionString(database);
            await store.MigrateAsync();

            Assert.Equal(beforeView.Append("sort_name|character varying"), await ColumnsAsync(database, "public_api", "officials"));
            Assert.Equal(beforeTables[0].Append("sort_name|character varying"), await ColumnsAsync(database, "public", tables[0]));
            Assert.Equal(beforeTables[1].Append("last_name|character varying"), await ColumnsAsync(database, "public", tables[1]));
            Assert.Equal(beforeTables[2].Append("last_name|character varying"), await ColumnsAsync(database, "public", tables[2]));
            Assert.Equal(["vh Chris Van Hollen"], await ReadAsPublicAsync(database, "SELECT official_id || ' ' || sort_name FROM public_api.officials"));
            var privateTable = await Assert.ThrowsAsync<PostgresException>(() => ReadAsPublicAsync(database, "SELECT sort_name FROM public.release_officials"));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, privateTable.SqlState);

            await db.GetService<IMigrator>().MigrateAsync(previous);
            Assert.Equal(beforeView, await ColumnsAsync(database, "public_api", "officials"));
            Assert.Equal(beforeTables, await Task.WhenAll(tables.Select(table => ColumnsAsync(database, "public", table))));
            Assert.Equal(["vh"], await ReadAsPublicAsync(database, "SELECT official_id FROM public_api.officials"));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private static async Task<string[]> ReadAsPublicAsync(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using (var role = new NpgsqlCommand("SET ROLE civic_lens_public", connection)) await role.ExecuteNonQueryAsync();
        return await RowsAsync(connection, sql);
    }

    // A dropped column keeps its ordinal position, so the names and types in order are compared, not the positions.
    private static async Task<string[]> ColumnsAsync(string database, string schema, string relation)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        return await RowsAsync(connection, $"""
            SELECT column_name || '|' || data_type FROM information_schema.columns
             WHERE table_schema = '{schema}' AND table_name = '{relation}' ORDER BY ordinal_position
            """);
    }

    private static async Task<string[]> RowsAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        return [.. rows];
    }
}
