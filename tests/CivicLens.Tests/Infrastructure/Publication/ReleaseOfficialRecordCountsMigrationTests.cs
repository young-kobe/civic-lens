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
public sealed class ReleaseOfficialRecordCountsMigrationTests(PostgresCollection postgres)
{
    private const string Migration = "20261116171634_ReleaseOfficialRecordCounts";

    // One statement names the official in release 1. The stored row says 7 records, so the view shows which source it reads.
    private const string SeedSql = """
        SET session_replication_role = replica;
        INSERT INTO release_officials (release_number, official_id, name) VALUES (1, 'p1', 'Ann Able');
        INSERT INTO publication_record_states (kind, record_id, first_release, revision_number) VALUES ('statement', 's1', 1, 1);
        INSERT INTO published_entry_officials (kind, record_id, revision_number, "position", official_id) VALUES ('statement', 's1', 1, 0, 'p1');
        """;

    [Fact]
    public async Task UpgradeMakesTheOfficialsViewReadTheStoredCountAndDowngradeRestoresTheLiveCountAndRevokesTheOwnerGrant()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);
            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, Migration) < 0);
            await ViewsSeed.ActivateReleaseAsync(database, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
            await ViewsSeed.ExecuteAsync(database, SeedSql);
            await ViewsSeed.ExecuteAsync(database, "INSERT INTO release_official_record_counts VALUES (1, 'p1', 7, 0, 0, 0, 0)");
            var upgraded = await ColumnsAsync(database);

            Assert.Equal(["p1 7"], await ReadAsPublicAsync(database, "SELECT official_id || ' ' || record_count FROM public_api.officials"));
            Assert.Equal(["p1 7"], await ReadAsPublicAsync(database, "SELECT official_id || ' ' || records FROM public_api.release_official_record_counts"));

            await db.GetService<IMigrator>().MigrateAsync(previous);
            Assert.Equal(upgraded, await ColumnsAsync(database));
            Assert.Equal(["p1 1"], await ReadAsPublicAsync(database, "SELECT official_id || ' ' || record_count FROM public_api.officials"));
            Assert.Equal(["true"], await ReadAsync(database, "SELECT (to_regclass('public_api.release_official_record_counts') IS NULL AND to_regclass('public.release_official_record_counts') IS NULL)::text"));

            await PostgresCollectionAttemptStore.FromConnectionString(database).MigrateAsync();
            Assert.Equal(["p1 0"], await ReadAsPublicAsync(database, "SELECT official_id || ' ' || record_count FROM public_api.officials"));
            Assert.Equal(["true"], await ReadAsync(database, "SELECT has_any_column_privilege('civic_lens_public_owner', 'release_official_record_counts', 'SELECT')::text"));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private static async Task<string[]> ColumnsAsync(string database) => await ReadAsync(database, """
        SELECT column_name || '|' || data_type FROM information_schema.columns
         WHERE table_schema = 'public_api' AND table_name = 'officials' ORDER BY ordinal_position
        """);

    private static async Task<string[]> ReadAsPublicAsync(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using (var role = new NpgsqlCommand("SET ROLE civic_lens_public", connection)) await role.ExecuteNonQueryAsync();
        return await RowsAsync(connection, sql);
    }

    private static async Task<string[]> ReadAsync(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        return await RowsAsync(connection, sql);
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
