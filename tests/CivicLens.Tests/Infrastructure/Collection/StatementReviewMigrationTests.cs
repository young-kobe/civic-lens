using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class StatementReviewMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string PreviousMigration = "20261012210000_AnalysisWakeupPayload";
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task BackfillMarksRevisionLessDraftsNotDraftedAndEveryOtherDraftAwaitingReview()
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>()
            .UseNpgsql(connectionString).Options);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await ExecuteAsync(connectionString, $"""
            SET session_replication_role = replica;
            INSERT INTO statement_drafts (candidate_id, current_revision_number, created_at_utc_ticks)
            VALUES ('{new string('1', 64)}', 0, 0), ('{new string('2', 64)}', 1, 0), ('{new string('3', 64)}', 3, 0);
            """);

        await db.Database.MigrateAsync();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT review_status, review_state_version FROM statement_drafts ORDER BY candidate_id", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string Status, int Version)>();
        while (await reader.ReadAsync()) rows.Add((reader.GetString(0), reader.GetInt32(1)));
        Assert.Equal([("NotDrafted", 0), ("AwaitingReview", 0), ("AwaitingReview", 0)], rows);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task TheDatabaseRefusesANotDraftedStatusOnADraftThatHasARevision()
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>()
            .UseNpgsql(connectionString).Options);
        await db.Database.MigrateAsync();

        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, $"""
            SET session_replication_role = replica;
            INSERT INTO statement_drafts (candidate_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks)
            VALUES ('{new string('4', 64)}', 1, 0, 'NotDrafted', 0);
            """));

        Assert.Equal("ck_statement_drafts_not_drafted", failure.ConstraintName);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
