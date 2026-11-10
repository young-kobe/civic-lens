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
public sealed class PublicApiVoteFirstPublicationsMigrationTests(PostgresCollection postgres)
{
    private const string PreviousMigration = "20261109203000_VoteMemberListTotals";

    [Fact]
    public async Task UpgradeGrantsTheViewThroughNormalSetupAndDowngradePreservesPublicationHistory()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await ViewsSeed.ActivateReleaseAsync(database, 1, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
            await ViewsSeed.ActivateReleaseAsync(database, 2, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
            await ViewsSeed.AddVotesAsync(database, new SeededVote('a', "2026-05-01") { FirstRelease = 2, ActiveRevision = 2 });
            await ViewsSeed.ExecuteAsync(database, $"""
                SET session_replication_role = replica;
                INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
                VALUES ('vote', '{new string('a', 64)}', 1, 1, 1);
                """);
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            await using (var connection = new NpgsqlConnection(database))
            {
                await connection.OpenAsync();
                await using var missing = new NpgsqlCommand("SELECT to_regclass('public_api.vote_first_publications') IS NULL", connection);
                Assert.True((bool)(await missing.ExecuteScalarAsync())!);
                await using var retained = new NpgsqlCommand("SELECT count(*) FROM publication_record_states WHERE kind='vote'", connection);
                Assert.Equal(2L, await retained.ExecuteScalarAsync());
            }

            await PostgresCollectionAttemptStore.FromConnectionString(database).MigrateAsync();
            await using var publicRead = new NpgsqlConnection(database);
            await publicRead.OpenAsync();
            await using (var role = new NpgsqlCommand("SET ROLE civic_lens_public", publicRead)) await role.ExecuteNonQueryAsync();
            await using var view = new NpgsqlCommand("SELECT revision_number || ' ' || release_number || ' ' || first_release FROM public_api.vote_first_publications", publicRead);
            Assert.Equal("2 2 1", await view.ExecuteScalarAsync());
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }
}
