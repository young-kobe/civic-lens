using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ActionReviewMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string PreviousMigration = "20261022160000_DerivedJobCharges";
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task BackfillPutsEveryDraftInReview()
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>()
            .UseNpgsql(connectionString).Options);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"""
                SET session_replication_role = replica;
                INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, created_at_utc_ticks)
                VALUES ('{new string('1', 64)}', 'bills', 119, 'hr', 1, 2, 5);
                INSERT INTO action_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
                VALUES ('{new string('1', 64)}', 1, 'auth0|a', 'Person', NULL, 'x', 10),
                       ('{new string('1', 64)}', 2, 'auth0|a', 'Person', NULL, 'x', 20);
                """, connection);
            await command.ExecuteNonQueryAsync();
        }

        await db.Database.MigrateAsync();

        await using var read = new NpgsqlConnection(connectionString);
        await read.OpenAsync();
        await using var query = new NpgsqlCommand(
            "SELECT review_status, review_state_version FROM action_drafts", read);
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(("AwaitingReview", 0), (reader.GetString(0), reader.GetInt32(1)));
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
