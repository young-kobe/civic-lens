using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiBillLookupMigrationTests(PostgresCollection postgres)
{
    private const string Migration = "20261107080000_PublicApiBillLookup";

    [Fact]
    public async Task UpgradeAndDowngradePreserveViewColumnTypesPositionsAndPublicReads()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);
            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, Migration) < 0);

            await db.GetService<IMigrator>().MigrateAsync(previous);
            var expected = await ColumnsAsync(database);

            await db.GetService<IMigrator>().MigrateAsync(Migration);
            Assert.Equal(expected, await ColumnsAsync(database));

            await db.GetService<IMigrator>().MigrateAsync(previous);
            Assert.Equal(expected, await ColumnsAsync(database));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private static async Task<string[]> ColumnsAsync(string database)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using (var read = new NpgsqlCommand("""
            SET ROLE civic_lens_public;
            SELECT record_id, sponsor_count, public_law, history_action_count FROM public_api.bills LIMIT 1;
            SELECT record_id, bill_record_id, alignment FROM public_api.votes LIMIT 1;
            """, connection)) await read.ExecuteNonQueryAsync();
        await using var columns = new NpgsqlCommand("""
            SELECT c.relname || '|' || a.attnum::text || '|' || a.attname || '|' || format_type(a.atttypid, a.atttypmod)
              FROM pg_attribute a
              JOIN pg_class c ON c.oid = a.attrelid
              JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE n.nspname = 'public_api' AND c.relname IN ('bills', 'votes')
               AND a.attnum > 0 AND NOT a.attisdropped
             ORDER BY c.relname, a.attnum
            """, connection);
        var result = new List<string>();
        await using var reader = await columns.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0));
        return [.. result];
    }
}
