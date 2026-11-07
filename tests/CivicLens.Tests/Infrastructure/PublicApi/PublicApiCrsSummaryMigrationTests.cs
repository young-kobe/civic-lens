using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiCrsSummaryMigrationTests(PostgresCollection postgres)
{
    [Fact]
    public async Task DowngradeRemovesCrsColumnsAndOwnerGrantsAndRestoresTheDependentPublicViews()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
            await using (var upgraded = new NpgsqlConnection(connectionString))
            {
                await upgraded.OpenAsync();
                await using var marker = new NpgsqlCommand("""
                    SELECT column_default = 'false' AND is_nullable = 'NO'
                      FROM information_schema.columns WHERE table_schema = 'public'
                       AND table_name = 'action_revisions' AND column_name = 'crs_summary_read'
                    """, upgraded);
                Assert.True((bool)(await marker.ExecuteScalarAsync())!);
                await using var privateMarker = new NpgsqlCommand("""
                    SELECT has_column_privilege('civic_lens_public_owner', 'action_revisions', 'crs_summary_read', 'SELECT')
                        OR has_column_privilege('civic_lens_public', 'action_revisions', 'crs_summary_read', 'SELECT')
                    """, upgraded);
                Assert.False((bool)(await privateMarker.ExecuteScalarAsync())!);
            }
            await db.GetService<IMigrator>().MigrateAsync("20261107210000_PublicApiOfficialPartyLines");
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var absent = new NpgsqlCommand("""
                SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public'
                    AND table_name = 'action_revisions' AND column_name IN ('crs_summary_text', 'crs_summary_read'))
                    OR has_column_privilege('civic_lens_public_owner', 'action_revisions', 'summary_version', 'SELECT')
                """, connection);
            Assert.False((bool)(await absent.ExecuteScalarAsync())!);
            await using var publicViews = new NpgsqlCommand("""
                SET ROLE civic_lens_public;
                SELECT public_law, history_action_count FROM public_api.bills LIMIT 1;
                SELECT * FROM public_api.bill_actions LIMIT 1;
                SELECT bill_record_id FROM public_api.votes LIMIT 1;
                SELECT * FROM public_api.brief_citations LIMIT 1;
                """, connection);
            await publicViews.ExecuteNonQueryAsync();
        }
        finally { await postgres.DropDatabaseAsync(connectionString); }
    }
}
