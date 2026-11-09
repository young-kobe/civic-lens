using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiDocumentChangeMigrationTests(PostgresCollection postgres)
{
    private const string Migration = "20261109152248_PublicApiDocumentChangeDetail";
    private static readonly string[] views = ["document_changes", "document_change_officials", "document_change_sources",
        "document_change_hunks", "document_change_word_edits", "document_change_citations"];

    [Fact]
    public async Task DowngradeRemovesOnlyDocumentViewsAndNormalMigrationRestoresTheirReadGrants()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);
            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, Migration) < 0);
            await db.GetService<IMigrator>().MigrateAsync(previous);
            await using (var connection = new NpgsqlConnection(database))
            {
                await connection.OpenAsync();
                await using var missing = new NpgsqlCommand("SELECT count(*) FROM unnest(@views) AS names(name) WHERE to_regclass('public_api.' || name) IS NOT NULL", connection);
                missing.Parameters.AddWithValue("views", views);
                Assert.Equal(0L, await missing.ExecuteScalarAsync());
                await using var baseline = new NpgsqlCommand("SELECT to_regclass('public_api.active_release') IS NOT NULL AND to_regclass('public_api.party_agreement') IS NOT NULL", connection);
                Assert.True((bool)(await baseline.ExecuteScalarAsync())!);
            }

            await PostgresCollectionAttemptStore.FromConnectionString(database).MigrateAsync();
            await using var verify = new NpgsqlConnection(database);
            await verify.OpenAsync();
            await using (var role = new NpgsqlCommand("SET ROLE civic_lens_public", verify))
                await role.ExecuteNonQueryAsync();
            foreach (var view in views)
            {
                await using var query = new NpgsqlCommand($"SELECT count(*) FROM public_api.{view}", verify);
                Assert.Equal(0L, await query.ExecuteScalarAsync());
            }
            await using var privateTable = new NpgsqlCommand("SELECT text FROM public.document_extractions", verify);
            var error = await Assert.ThrowsAsync<PostgresException>(() => privateTable.ExecuteScalarAsync());
            Assert.Equal("42501", error.SqlState);
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }
}
