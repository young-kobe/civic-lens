using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class AttemptElapsedMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Migration = "20261104012000_AttemptElapsed";
    private const long CutoffTicks = 500;

    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
        await MigrateToAsync(Before());
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task ARowObservedUpToTheNewestOldRowMayHaveNoElapsedTimeButALaterRowMayNotBecauseOnlyOldRowsLackAMeasurement()
    {
        await InsertAttemptAsync("old-early", 100);
        await InsertAttemptAsync("old-newest", CutoffTicks);

        await MigrateToAsync(Migration);

        Assert.Equal(["old-early|<null>", "old-newest|<null>"], await ReadAsync(
            "SELECT attempt_id || '|' || coalesce(elapsed_milliseconds::text, '<null>') FROM collection_attempts ORDER BY observed_at_utc_ticks"));
        await InsertAttemptAsync("old-late-write", CutoffTicks - 1);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => InsertAttemptAsync("new-unmeasured", CutoffTicks + 1));
        Assert.Equal("ck_collection_attempts_elapsed_measured", failure.ConstraintName);
        await InsertAttemptAsync("new-measured", CutoffTicks + 1, elapsedMilliseconds: 250);
    }

    private Task InsertAttemptAsync(string attemptId, long observedTicks, long? elapsedMilliseconds = null)
    {
        var hasElapsedColumn = elapsedMilliseconds is not null;
        return ExecuteAsync($"""
            INSERT INTO collection_attempts (attempt_id, source_id, requested_url, final_url, observed_at_utc_ticks, outcome, has_response,
                has_sent_validators, failure_code{(hasElapsedColumn ? ", elapsed_milliseconds" : "")})
            VALUES ('{attemptId}', 'source', 'https://example.test/pages/a', 'https://example.test/pages/a', {observedTicks}, 'failed', FALSE, FALSE,
                'HttpError'{(hasElapsedColumn ? $", {elapsedMilliseconds}" : "")})
            """);
    }

    private string Before() => NewContext().Database.GetMigrations().TakeWhile(id => id != Migration).Last();

    private CollectionAttemptDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);

    private async Task MigrateToAsync(string target)
    {
        await using var db = NewContext();
        await db.GetService<IMigrator>().MigrateAsync(target);
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
