using CivicLens.Tests.Fixtures;
using System.Diagnostics;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.Jobs;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresCollectionPipelineWakeupTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
    }

    [Fact]
    public async Task NotificationWakesListenerAfterSubscription()
    {
        await using var wakeup = PostgresCollectionPipelineWakeup.FromConnectionString(connectionString);
        await wakeup.ConnectAsync(default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waiting = wakeup.WaitAsync(timeout.Token);
        await InsertJobAsync("notified", DateTimeOffset.UtcNow.UtcTicks, null);
        await waiting;
    }

    [Fact]
    public async Task DeadlineThatPassesAfterConnectWakesWithoutNotification()
    {
        await InsertJobAsync("deadline", DateTimeOffset.UtcNow.UtcTicks, null);
        await using var wakeup = PostgresCollectionPipelineWakeup.FromConnectionString(connectionString);
        await wakeup.ConnectAsync(default);
        // Lease updates are silent. Set the deadline to the database now after subscription,
        // so it is already past when the wait starts and no notification can announce it.
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                UPDATE collection_jobs SET lease_token = 'deadline-lease', lease_expires_at =
                    (EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968000000000)::bigint
                WHERE job_id = 'deadline'
                """, connection);
            await command.ExecuteNonQueryAsync();
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var elapsed = Stopwatch.StartNew();
        await wakeup.WaitAsync(timeout.Token);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ReleasingRunnableClaimWakesAfterQueueSkippedItsLease()
    {
        var jobs = PostgresCollectionJobStore.FromConnectionString(connectionString);
        var job = await jobs.EnqueueAsync(Source(), "release-runnable", default);
        var claim = await jobs.TryClaimAsync(job.JobId, TimeSpan.FromSeconds(30), default);
        Assert.NotNull(claim);
        await using var wakeup = PostgresCollectionPipelineWakeup.FromConnectionString(connectionString);
        await wakeup.ConnectAsync(default);
        var queue = PostgresCollectionWorkerQueue.FromConnectionString(connectionString);
        Assert.Empty((await queue.GetEligibleJobIdsAsync(null, 20, default)).JobIds);

        Assert.True(await jobs.ReleaseClaimAsync(claim.Lease, default));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await wakeup.WaitAsync(timeout.Token);
        Assert.Contains(job.JobId, (await queue.GetEligibleJobIdsAsync(null, 20, default)).JobIds);
    }

    [Fact]
    public async Task ReleasingOriginBlockedClaimDoesNotWakeItself()
    {
        var jobs = PostgresCollectionJobStore.FromConnectionString(connectionString);
        var job = await jobs.EnqueueAsync(Source(), "release-blocked", default);
        var claim = await jobs.TryClaimAsync(job.JobId, TimeSpan.FromSeconds(30), default);
        Assert.NotNull(claim);
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "INSERT INTO collection_job_origins (origin, not_before) VALUES ('https://example.test:443', @due)", connection);
            command.Parameters.AddWithValue("due", DateTimeOffset.UtcNow.AddMinutes(1).UtcTicks);
            await command.ExecuteNonQueryAsync();
        }
        await using var wakeup = PostgresCollectionPipelineWakeup.FromConnectionString(connectionString);
        await wakeup.ConnectAsync(default);
        Assert.True(await jobs.ReleaseClaimAsync(claim.Lease, default));

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wakeup.WaitAsync(timeout.Token));
    }

    [Fact]
    public async Task ReleaseCommittedAfterBarrierDeadlineStillWakesListener()
    {
        var jobs = PostgresCollectionJobStore.FromConnectionString(connectionString);
        var job = await jobs.EnqueueAsync(Source(), "late-release", default);
        var claim = await jobs.TryClaimAsync(job.JobId, TimeSpan.FromSeconds(30), default);
        Assert.NotNull(claim);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var control = new NpgsqlConnection(connectionString);
        await control.OpenAsync(timeout.Token);
        await using (var setup = new NpgsqlCommand("""
            SELECT pg_advisory_lock(71433, 1);
            CREATE FUNCTION hold_claim_release() RETURNS trigger AS $$
            BEGIN
                PERFORM pg_advisory_xact_lock(71433, 1);
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER hold_claim_release BEFORE UPDATE OF lease_token ON collection_jobs
            FOR EACH ROW WHEN (NEW.lease_token IS NULL AND OLD.lease_token IS NOT NULL)
            EXECUTE FUNCTION hold_claim_release();
            INSERT INTO collection_job_origins (origin, not_before)
            VALUES ('https://example.test:443',
                (EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968010000000)::bigint);
            """, control))
            await setup.ExecuteNonQueryAsync(timeout.Token);
        await using var wakeup = PostgresCollectionPipelineWakeup.FromConnectionString(connectionString);
        await wakeup.ConnectAsync(timeout.Token);
        var release = jobs.ReleaseClaimAsync(claim.Lease, timeout.Token);
        try
        {
            await using var blocked = new NpgsqlCommand("""
                SELECT EXISTS (SELECT 1 FROM pg_locks WHERE locktype = 'advisory'
                    AND classid = 71433 AND objid = 1 AND NOT granted)
                """, control);
            while (!(bool)(await blocked.ExecuteScalarAsync(timeout.Token))!)
                await Task.Delay(10, timeout.Token);
            // Cross the pacing deadline and consume it while release is still uncommitted.
            await wakeup.WaitAsync(timeout.Token);
            await wakeup.WaitAsync(timeout.Token);
            var queue = PostgresCollectionWorkerQueue.FromConnectionString(connectionString);
            Assert.Empty((await queue.GetEligibleJobIdsAsync(null, 20, timeout.Token)).JobIds);
        }
        finally
        {
            await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(71433, 1)", control);
            await unlock.ExecuteNonQueryAsync();
        }
        Assert.True(await release);
        using var releaseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await wakeup.WaitAsync(releaseTimeout.Token);
    }

    private static ConfiguredCollectionSource Source() =>
        CollectionFixtures.PageSource(pathPrefix: "/", policy: new CollectionJobPolicy());

    private async Task InsertJobAsync(string id, long createdAt, long? retryAt)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO collection_jobs (job_id, idempotency_key, definition_json, source_id, is_root, pacing_key, state, created_at,
                retry_at, cancellation_requested, lease_fence)
            VALUES (@id, @key, '{}', 'source', FALSE, 'source', @state, @created, @retry, FALSE, 0)
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("key", "key-" + id);
        command.Parameters.AddWithValue("state", retryAt is null ? "Pending" : "WaitingToRetry");
        command.Parameters.AddWithValue("created", createdAt);
        command.Parameters.AddWithValue("retry", (object?)retryAt ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }
}
