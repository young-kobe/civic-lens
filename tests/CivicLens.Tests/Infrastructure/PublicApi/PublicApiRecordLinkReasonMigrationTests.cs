using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiRecordLinkReasonMigrationTests(PostgresCollection postgres)
{
    [Fact]
    public async Task DowngradeRestoresThePriorLinkViewAndRemovesItsAddedOwnerGrant()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, "20261107060000_PublicApiRecordLinkReason") < 0);
            await db.GetService<IMigrator>().MigrateAsync(previous);
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var removed = new NpgsqlCommand("""
                SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public_api'
                    AND table_name = 'record_links' AND column_name = 'reason')
                    OR has_column_privilege('civic_lens_public_owner', 'relationship_revisions', 'reason', 'SELECT')
                """, connection);
            Assert.False((bool)(await removed.ExecuteScalarAsync())!);
            await using var restored = new NpgsqlCommand("""
                SET ROLE civic_lens_public;
                SELECT link_record_id FROM public_api.record_links LIMIT 1;
                """, connection);
            await restored.ExecuteNonQueryAsync();
        }
        finally { await postgres.DropDatabaseAsync(connectionString); }
    }
}
