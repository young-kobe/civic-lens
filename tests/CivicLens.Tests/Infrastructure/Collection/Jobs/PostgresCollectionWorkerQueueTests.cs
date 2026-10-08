using CivicLens.Application.Collection.Jobs;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.Jobs;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresCollectionWorkerQueueTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly string schema = "worker_queue_" + Guid.NewGuid().ToString("N");
    private string connectionString = null!;
    private PostgresCollectionJobStore jobs = null!;
    private PostgresCollectionWorkerQueue queue = null!;

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE SCHEMA {schema}", connection);
        await command.ExecuteNonQueryAsync();
        connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { SearchPath = schema }.ConnectionString;
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        var factory = new PooledDbContextFactory<CollectionAttemptDbContext>(options);
        jobs = PostgresCollectionJobStore.FromConnectionString(connectionString);
        queue = new PostgresCollectionWorkerQueue(factory);
        await PostgresCollectionAttemptStore.FromConnectionString(connectionString).MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {schema} CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task FindsDueAndRecoverableJobsPastTerminalRowsAndPagesWithoutDuplicates()
    {
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                INSERT INTO collection_jobs (job_id, idempotency_key, definition_json, state, created_at,
                    retry_at, cancellation_requested, charged_requests, charged_bytes, charged_seconds,
                    lease_fence)
                SELECT 'terminal-' || value::text, 'terminal-key-' || value::text, '{}', 'Succeeded',
                    @created_at + value, NULL, FALSE, 0, 0, 0, 0
                FROM generate_series(1, 125) AS value
                """, connection);
            command.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow.UtcDateTime.Ticks);
            await command.ExecuteNonQueryAsync();
        }

        var pending = await EnqueueAsync("pending");
        var dueRetry = await EnqueueAsync("due-retry");
        var expiredRunning = await EnqueueAsync("expired-running");
        var futureRetry = await EnqueueAsync("future-retry");
        var liveLease = await EnqueueAsync("live-lease");
        var now = DateTimeOffset.UtcNow.UtcDateTime.Ticks;
        await ExecuteAsync("UPDATE collection_jobs SET state = 'WaitingToRetry', retry_at = @retry WHERE job_id = @id",
            new NpgsqlParameter("retry", now - TimeSpan.TicksPerMinute), new NpgsqlParameter("id", dueRetry.JobId));
        await ExecuteAsync("UPDATE collection_jobs SET state = 'Running', lease_token = 'expired-token', lease_fence = 1, lease_expires_at = @expires WHERE job_id = @id",
            new NpgsqlParameter("expires", now - TimeSpan.TicksPerMinute), new NpgsqlParameter("id", expiredRunning.JobId));
        await ExecuteAsync("UPDATE collection_jobs SET state = 'WaitingToRetry', retry_at = @retry WHERE job_id = @id",
            new NpgsqlParameter("retry", now + TimeSpan.TicksPerHour), new NpgsqlParameter("id", futureRetry.JobId));
        await ExecuteAsync("UPDATE collection_jobs SET state = 'Running', lease_token = 'live-token', lease_fence = 1, lease_expires_at = @expires WHERE job_id = @id",
            new NpgsqlParameter("expires", now + TimeSpan.TicksPerHour), new NpgsqlParameter("id", liveLease.JobId));

        var first = await queue.GetEligibleJobIdsAsync(null, 2, CancellationToken.None);
        Assert.Equal(2, first.JobIds.Length);
        Assert.NotNull(first.NextCursor);
        var second = await queue.GetEligibleJobIdsAsync(first.NextCursor, 2, CancellationToken.None);
        Assert.Null(second.NextCursor);
        var selected = first.JobIds.Concat(second.JobIds).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(3, selected.Count);
        Assert.Contains(pending.JobId, selected);
        Assert.Contains(dueRetry.JobId, selected);
        Assert.Contains(expiredRunning.JobId, selected);
        Assert.DoesNotContain(futureRetry.JobId, selected);
        Assert.DoesNotContain(liveLease.JobId, selected);
        Assert.DoesNotContain(selected, id => id.StartsWith("terminal-", StringComparison.Ordinal));
    }

    private Task<CollectionJobRecord> EnqueueAsync(string key) => jobs.EnqueueAsync(new CollectionJobDefinition
    {
        SourceId = "source",
        Url = "https://example.test/page",
        AllowedOrigin = "https://example.test",
        AllowedPathPrefix = "/page",
        MaxRequests = 5,
        MaxBytes = 100,
        TimeoutSeconds = 10,
        MinDelayMilliseconds = 0,
        Policy = new CollectionJobPolicy { InitialRetryDelaySeconds = 0 }
    }, key, CancellationToken.None);

    private async Task ExecuteAsync(string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }
}
