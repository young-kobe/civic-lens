using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiStatementListsMigrationTests(PostgresCollection postgres)
{
    [Fact]
    public async Task DowngradeDropsTheViewsAndMigrationRestoresThemAndTheirMissingColumnGrants()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);
            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, "20261107170000_PublicApiStatementLists") < 0);
            await db.GetService<IMigrator>().MigrateAsync(previous);
            await using var connection = new NpgsqlConnection(database);
            await connection.OpenAsync();
            await using (var absent = new NpgsqlCommand("SELECT to_regclass('public_api.statements') IS NULL AND to_regclass('public_api.statement_speakers') IS NULL", connection))
                Assert.Equal(true, await absent.ExecuteScalarAsync());

            var store = PostgresCollectionAttemptStore.FromConnectionString(database);
            await store.MigrateAsync();
            await ReadViewsAsync(connection);
            await using (var revoke = new NpgsqlCommand("""
                REVOKE SELECT (statement_date, summary) ON statement_revisions FROM civic_lens_public_owner;
                REVOKE SELECT (candidate_id, revision_number, official_id, "position") ON statement_revision_speakers FROM civic_lens_public_owner;
                REVOKE SELECT (candidate_id, revision_number, issue_id, "position") ON statement_revision_issues FROM civic_lens_public_owner;
                """, connection)) await revoke.ExecuteNonQueryAsync();
            var denied = await Assert.ThrowsAsync<PostgresException>(() => ReadViewsAsync(connection));
            Assert.Equal("42501", denied.SqlState);

            await store.MigrateAsync();
            await ReadViewsAsync(connection);
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task PublicRoleReadsOnlyTheListedStatementViewColumns()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var connection = new NpgsqlConnection(database);
            await connection.OpenAsync();
            await using (var read = new NpgsqlCommand("SET ROLE civic_lens_public; SELECT record_id, revision_number, statement_date, headline, summary, text_origin, label, issue_ids FROM public_api.statements LIMIT 0; SELECT record_id, revision_number, \"position\", official_id FROM public_api.statement_speakers LIMIT 0;", connection))
                await read.ExecuteNonQueryAsync();
            var exception = await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                await using var denied = new NpgsqlCommand("SELECT candidate_id FROM public.statement_revisions LIMIT 0", connection);
                await denied.ExecuteNonQueryAsync();
            });
            Assert.Equal("42501", exception.SqlState);
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private static async Task ReadViewsAsync(NpgsqlConnection connection)
    {
        try
        {
            await using var read = new NpgsqlCommand("""
                SET ROLE civic_lens_public;
                SELECT record_id, summary, statement_date, issue_ids FROM public_api.statements LIMIT 0;
                SELECT record_id, official_id, "position" FROM public_api.statement_speakers LIMIT 0;
                """, connection);
            await read.ExecuteNonQueryAsync();
        }
        finally
        {
            await using var reset = new NpgsqlCommand("RESET ROLE", connection);
            await reset.ExecuteNonQueryAsync();
        }
    }
}
