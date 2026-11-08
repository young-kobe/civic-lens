using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiActiveReleaseIdentityMigrationTests(PostgresCollection postgres)
{
    private const string Migration = "20261108135000_PublicApiActiveReleaseIdentity";

    [Fact]
    public async Task ShowsIdentityForAnEmptyReleaseToTheReadRoleAndDropsOnlyItsViewOnDowngrade()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, Migration) < 0);
            await db.GetService<IMigrator>().MigrateAsync(previous);
            await PostgresCollectionAttemptStore.FromConnectionString(connectionString).MigrateAsync();
            await ViewsSeed.ActivateReleaseAsync(connectionString, 2, new DateTimeOffset(2026, 6, 9, 12, 30, 0, TimeSpan.Zero), recordCount: 0);

            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using (var role = new NpgsqlCommand("SET ROLE civic_lens_public", connection))
                    await role.ExecuteNonQueryAsync();
                await using var query = new NpgsqlCommand("SELECT release_number || ' ' || published_at_utc_ticks FROM public_api.active_release", connection);
                Assert.Equal("2 639166050000000000", await query.ExecuteScalarAsync());
                await using var privateColumn = new NpgsqlCommand("SELECT record_count FROM public_api.active_release", connection);
                var error = await Assert.ThrowsAsync<PostgresException>(() => privateColumn.ExecuteScalarAsync());
                Assert.Equal("42703", error.SqlState);
            }

            await db.GetService<IMigrator>().MigrateAsync(previous);
            await using var verify = new NpgsqlConnection(connectionString);
            await verify.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT to_regclass('public_api.active_release') IS NULL", verify);
            Assert.True((bool)(await command.ExecuteScalarAsync())!);
            await using var records = new NpgsqlCommand("SELECT to_regclass('public_api.active_records') IS NOT NULL", verify);
            Assert.True((bool)(await records.ExecuteScalarAsync())!);
        }
        finally { await postgres.DropDatabaseAsync(connectionString); }
    }
}
