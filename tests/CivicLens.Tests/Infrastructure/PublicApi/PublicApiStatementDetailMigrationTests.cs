using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiStatementDetailMigrationTests(PostgresCollection postgres)
{
    [Fact]
    public async Task DowngradeRemovesOnlyEvidenceViewsAndMigrationRestoresTheirOwnerColumnGrants()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>()
                .UseNpgsql(database).Options);
            var previous = db.Database.GetMigrations()
                .Last(id => string.CompareOrdinal(id, "20261108020000_PublicApiStatementDetail") < 0);
            await db.GetService<IMigrator>().MigrateAsync(previous);
            await using var connection = new NpgsqlConnection(database);
            await connection.OpenAsync();
            await using (var absent = new NpgsqlCommand("""
                SELECT to_regclass('public_api.statement_sources') IS NULL
                   AND to_regclass('public_api.statement_quotations') IS NULL
                   AND to_regclass('public_api.statements') IS NOT NULL
                   AND to_regclass('public_api.statement_speakers') IS NOT NULL
                """, connection))
                Assert.Equal(true, await absent.ExecuteScalarAsync());

            var store = PostgresCollectionAttemptStore.FromConnectionString(database);
            await store.MigrateAsync();
            await ReadEvidenceViewsAsync(connection);
            await using (var revoke = new NpgsqlCommand("""
                REVOKE SELECT (url) ON statement_candidates FROM civic_lens_public_owner;
                REVOKE SELECT (text) ON document_extractions FROM civic_lens_public_owner;
                REVOKE SELECT (requested_url) ON collection_attempts FROM civic_lens_public_owner;
                REVOKE SELECT (statement_date_evidence_start) ON statement_revisions FROM civic_lens_public_owner;
                REVOKE SELECT (quotation_start) ON statement_revision_quotations FROM civic_lens_public_owner;
                """, connection))
                await revoke.ExecuteNonQueryAsync();
            var denied = await Assert.ThrowsAsync<PostgresException>(() => ReadEvidenceViewsAsync(connection));
            Assert.Equal("42501", denied.SqlState);

            await store.MigrateAsync();
            await ReadEvidenceViewsAsync(connection);
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private static async Task ReadEvidenceViewsAsync(NpgsqlConnection connection)
    {
        try
        {
            await using var read = new NpgsqlCommand("""
                SET ROLE civic_lens_public;
                SELECT record_id, revision_number, extraction_id, url, observed_at_utc_ticks, text, article_url,
                       statement_date_evidence_extraction_id, statement_date_evidence_start, statement_date_evidence_length
                  FROM public_api.statement_sources LIMIT 0;
                SELECT record_id, revision_number, position, extraction_id, quotation_start, quotation_length, kind
                  FROM public_api.statement_quotations LIMIT 0;
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
