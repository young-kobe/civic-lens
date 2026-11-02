using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Review;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class RejectDecisionMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Migration = "20261102180637_RejectDecision";

    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task TheMigrationGoesDownWhenNoRecordWasRejectedAndRemovesTheReasonCodeColumns()
    {
        await RollBackAsync();

        Assert.Empty(await ReadAsync("SELECT 1 FROM information_schema.columns WHERE column_name = 'reason_code'"));
        Assert.Empty(await ReadAsync("SELECT 1 FROM pg_constraint WHERE conname LIKE 'ck_%_review_decisions_reason_code%'"));
        Assert.DoesNotContain(Migration, await ReadAsync("""SELECT "MigrationId" FROM "__EFMigrationsHistory" """));
    }

    [Theory]
    [InlineData("document_change", "draft_id")]
    [InlineData("statement", "candidate_id")]
    [InlineData("action", "record_id")]
    [InlineData("vote", "record_id")]
    [InlineData("executive_action", "record_id")]
    [InlineData("relationship", "record_id")]
    [InlineData("brief", "record_id")]
    public async Task ARejectionDecisionStopsTheRollbackAndLeavesTheSchemaAsItWasBecauseAnOlderImageCannotParseIt(string prefix, string idColumn)
    {
        await ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO {prefix}_review_decisions (decision_id, {idColumn}, revision_number, review_state_version, kind, actor_subject, created_at_utc_ticks, reason_code)
            VALUES (repeat('a', 32), 'r1', 1, 1, 'Reject', 'auth0|reviewer', 1, 'Duplicate')
            """);

        await AssertRollbackFailsAndChangesNothingAsync();
    }

    [Fact]
    public async Task ARejectedDraftStopsTheRollbackAndLeavesTheSchemaAsItWasBecauseAnOlderImageCannotParseTheStatus()
    {
        await ExecuteAsync("""
            SET session_replication_role = replica;
            INSERT INTO statement_drafts (candidate_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES (repeat('1', 64), 1, 1, 'Rejected', 0, 0)
            """);

        await AssertRollbackFailsAndChangesNothingAsync();
    }

    private async Task AssertRollbackFailsAndChangesNothingAsync()
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(RollBackAsync);

        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        Assert.Equal(7, (await ReadAsync("SELECT table_name FROM information_schema.columns WHERE column_name = 'reason_code'")).Count);
        Assert.Contains(Migration, await ReadAsync("""SELECT "MigrationId" FROM "__EFMigrationsHistory" """));
    }

    private async Task RollBackAsync()
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        await db.GetService<IMigrator>().MigrateAsync(db.Database.GetMigrations().TakeWhile(id => id != Migration).Last());
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> ReadAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.GetValue(0).ToString()!);
        return rows;
    }
}
