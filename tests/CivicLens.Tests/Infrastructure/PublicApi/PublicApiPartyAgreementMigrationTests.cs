using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiPartyAgreementMigrationTests(PostgresCollection postgres)
{
    private const string Migration = "20261108140000_PublicApiPartyAgreement";

    [Fact]
    public async Task CreatesRestrictedReadViewAndRemovesItOnDowngrade()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, Migration) < 0);
            await db.GetService<IMigrator>().MigrateAsync(previous);
            await db.GetService<IMigrator>().MigrateAsync(Migration);

            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var query = new NpgsqlCommand("SET ROLE civic_lens_public; SELECT release_number, chamber, congress, kind, agreed, split, mixed FROM public_api.party_agreement LIMIT 0", connection);
                await query.ExecuteNonQueryAsync();
            }

            await db.GetService<IMigrator>().MigrateAsync(previous);
            await using var verify = new NpgsqlConnection(connectionString);
            await verify.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT to_regclass('public_api.party_agreement')", verify);
            Assert.Null(await command.ExecuteScalarAsync());
        }
        finally { await postgres.DropDatabaseAsync(connectionString); }
    }
}
