using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicReadIndexesMigrationTests(PostgresCollection postgres)
{
    [Fact]
    public async Task IndexesUpgradeAndDowngradeWithoutChangingPublicRoleBoundary()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);
            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, "20261107180000_PublicReadIndexes") < 0);

            await db.GetService<IMigrator>().MigrateAsync(previous);
            Assert.Empty(await IndexDefinitionsAsync(database));

            await PostgresCollectionAttemptStore.FromConnectionString(database).MigrateAsync();
            var definitions = await IndexDefinitionsAsync(database);
            Assert.Equal(2, definitions.Length);
            Assert.Contains(definitions, definition => definition.Contains("(congress, bill_number, bill_type, record_id, revision_number)", StringComparison.Ordinal)
                && definition.Contains("WHERE ((headline IS NOT NULL) AND (event_on IS NOT NULL))", StringComparison.Ordinal));
            Assert.Contains(definitions, definition => definition.Contains("(list_id) INCLUDE (vote_cast)", StringComparison.Ordinal));

            await using var connection = new NpgsqlConnection(database);
            await connection.OpenAsync();
            await using (var allowed = new NpgsqlCommand("SET ROLE civic_lens_public; SELECT record_id FROM public_api.votes LIMIT 0; SELECT record_id FROM public_api.bills LIMIT 0;", connection))
                await allowed.ExecuteNonQueryAsync();
            await using (var denied = new NpgsqlCommand("SELECT record_id FROM public.action_revisions LIMIT 0", connection))
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => denied.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
            }
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private static async Task<string[]> IndexDefinitionsAsync(string database)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT pg_get_indexdef(indexrelid)
              FROM pg_index
              JOIN pg_class c ON c.oid = indexrelid
              JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE n.nspname = 'public'
               AND c.relname IN ('ix_action_revisions_bill_identity', 'ix_vote_member_list_entries_tallies')
             ORDER BY c.relname
            """, connection);
        var definitions = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) definitions.Add(reader.GetString(0));
        return [.. definitions];
    }
}
