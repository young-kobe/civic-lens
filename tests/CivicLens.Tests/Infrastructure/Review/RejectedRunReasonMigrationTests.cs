using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Review;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class RejectedRunReasonMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Migration = "20261102233609_RejectedRunReason";

    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Theory]
    [InlineData("action")]
    [InlineData("vote")]
    public async Task ARunKeepsTheReasonThatRecordsARefusalBecauseTheCheckAllowsIt(string prefix)
    {
        await InsertRunAsync(prefix, "recordRejected");

        Assert.Equal(["recordRejected"], await ReadAsync($"SELECT reason FROM {prefix}_analysis_runs"));
    }

    [Theory]
    [InlineData("action")]
    [InlineData("vote")]
    public async Task TheMigrationGoesDownWithoutARefusalAndThenTheTableRefusesTheReason(string prefix)
    {
        await RollBackAsync();

        var failure = await Assert.ThrowsAsync<PostgresException>(() => InsertRunAsync(prefix, "recordRejected"));

        Assert.Equal($"ck_{prefix}_analysis_runs_reason", failure.ConstraintName);
        Assert.DoesNotContain(Migration, await ReadAsync("""SELECT "MigrationId" FROM "__EFMigrationsHistory" """));
    }

    [Theory]
    [InlineData("action")]
    [InlineData("vote")]
    public async Task ARefusalRowStopsTheRollbackAndLeavesTheSchemaAsItWasBecauseNoRunIsDeleted(string prefix)
    {
        await InsertRunAsync(prefix, "recordRejected");

        var failure = await Assert.ThrowsAsync<PostgresException>(RollBackAsync);

        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        Assert.Contains(Migration, await ReadAsync("""SELECT "MigrationId" FROM "__EFMigrationsHistory" """));
    }

    private async Task RollBackAsync()
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        await db.GetService<IMigrator>().MigrateAsync(db.Database.GetMigrations().TakeWhile(id => id != Migration).Last());
    }

    private async Task InsertRunAsync(string prefix, string reason)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""
            SET session_replication_role = replica;
            INSERT INTO {prefix}_analysis_runs (run_id, candidate_id, task, task_version, model, prompt_version, schema_version, input_hash, outcome, reason,
                input_tokens, output_tokens, cache_read_tokens, cache_write_tokens, charged_tokens, started_at_utc_ticks, finished_at_utc_ticks)
            VALUES (repeat('a', 32), 'c1', '{prefix}-draft', 'v', 'm', 'p', 's', repeat('b', 64), 'Bypassed', @reason, 0, 0, 0, 0, 0, 1, 1)
            """, connection);
        command.Parameters.AddWithValue("reason", reason);
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
