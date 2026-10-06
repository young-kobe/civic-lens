using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.Jobs;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresCollectionJobStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly string schema = "jobs_" + Guid.NewGuid().ToString("N");
    private readonly string root = Path.Combine(Path.GetTempPath(), "civic-jobs-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PostgresCollectionJobStore jobs = null!;
    private PostgresCollectionAttemptStore evidence = null!;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE SCHEMA {schema}", connection);
        await command.ExecuteNonQueryAsync();
        connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { SearchPath = schema }.ConnectionString;
        jobs = PostgresCollectionJobStore.FromConnectionString(connectionString);
        evidence = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        await evidence.MigrateAsync();
        Directory.CreateDirectory(root);
    }

    public async Task DisposeAsync()
    {
        Directory.Delete(root, true);
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task SettlementRejectsDifferentEvidenceBeforeRefundingBudget()
    {
        var claim = await EnqueueClaimAsync("binding", Definition());
        var start = await jobs.TryStartAttemptAsync(claim.Lease, root, default);
        var receipt = ReceiptCollector.Result(start.Attempt!.Request, 404);
        await CollectionAttemptImporter.ImportAsync(start.Attempt.AttemptId, start.Attempt.Request, receipt, evidence, default);
        var altered = receipt with { FinalUrl = receipt.FinalUrl + "/other" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => jobs.SettleAttemptAsync(claim.Lease,
            start.Attempt.AttemptId, CollectionJobLifecycle.Resolve(altered), default));
        var unchanged = (await jobs.GetAsync(claim.Job.JobId, default))!;
        Assert.Equal(5, unchanged.ReservedRequests);
        Assert.Null(Assert.Single(unchanged.Attempts).Resolution);
        Assert.True(await jobs.SettleAttemptAsync(claim.Lease, start.Attempt.AttemptId,
            CollectionJobLifecycle.Resolve(receipt), default));
    }

    [Fact]
    public async Task EnqueueIsIdempotentAndConflictingKeysCannotReplaceSnapshots()
    {
        var definition = Definition();
        var enqueued = await Task.WhenAll(jobs.EnqueueAsync(definition, "request", default),
            jobs.EnqueueAsync(definition, "request", default));
        Assert.Equal(enqueued[0].JobId, enqueued[1].JobId);
        await Assert.ThrowsAsync<ArgumentException>(() => jobs.EnqueueAsync(definition with { MaxBytes = 99 }, "request", default));
        var stored = Assert.Single(await jobs.ListAsync(10, default));
        Assert.Equal(definition, stored.Definition);
        Assert.Empty(stored.Attempts);
    }

    [Fact]
    public async Task SingletonAdmissionSurvivesDifferentRootsAndOriginBarrierOutlivesCollectorRelease()
    {
        var first = await EnqueueClaimAsync("first", Definition());
        var other = await EnqueueClaimAsync("other", Definition("https://other.test"));
        var starts = await Task.WhenAll(jobs.TryStartAttemptAsync(first.Lease, root, default),
            jobs.TryStartAttemptAsync(other.Lease, root + "-other", default));
        Assert.Single(starts, start => start.Status == CollectionJobStartStatus.Started);
        Assert.Single(starts, start => start.BlockReason == CollectionJobBlockReason.CollectorBusy);
        var winner = starts[0].Status == CollectionJobStartStatus.Started ? first : other;
        var active = starts.Single(start => start.Status == CollectionJobStartStatus.Started);
        Assert.True(await jobs.ReleaseCollectorAsync(winner.Lease, active.CollectorLease!, default));

        var sameOrigin = await EnqueueClaimAsync("same", winner.Job.Definition);
        var blocked = await jobs.TryStartAttemptAsync(sameOrigin.Lease, root, default);
        Assert.Equal(CollectionJobBlockReason.AttemptUnresolved, blocked.BlockReason);
        Assert.Empty((await jobs.GetAsync(sameOrigin.Job.JobId, default))!.Attempts);
    }

    [Fact]
    public async Task ExpiredOwnerCannotRenewSettleOrReleaseNewOwnershipAndReservationsSurvive()
    {
        var original = await EnqueueClaimAsync("expired", Definition());
        var start = await jobs.TryStartAttemptAsync(original.Lease, root, default);
        await SqlAsync("UPDATE collection_jobs SET lease_expires_at = 1; UPDATE collection_collector_slot SET expires_at = 1;");
        var recovered = (await jobs.TryClaimAsync(original.Job.JobId, LeaseDuration, default))!;
        Assert.True(recovered.Lease.Fence > original.Lease.Fence);
        Assert.False((await jobs.RenewAsync(original.Lease, start.CollectorLease, LeaseDuration, default)).Owned);
        Assert.False(await jobs.ReleaseClaimAsync(original.Lease, default));
        Assert.False(await jobs.ReleaseCollectorAsync(original.Lease, start.CollectorLease!, default));
        var interrupted = new CollectionAttemptResolution(CollectionJobAttemptOutcome.Interrupted, null, "NoReceipt");
        Assert.False(await jobs.SettleAttemptAsync(original.Lease, start.Attempt!.AttemptId, interrupted, default));
        var competing = await EnqueueClaimAsync("competing", Definition());
        Assert.Equal(CollectionJobBlockReason.AttemptUnresolved,
            (await jobs.TryStartAttemptAsync(competing.Lease, root, default)).BlockReason);
        Assert.True(await jobs.SettleAttemptAsync(recovered.Lease, start.Attempt.AttemptId, interrupted, default));
        var retained = (await jobs.GetAsync(original.Job.JobId, default))!;
        Assert.Equal(5, retained.ReservedRequests);
        Assert.Equal(100, retained.ReservedBytes);
        Assert.Equal(10, retained.ReservedTimeoutSeconds);
        Assert.Equal(CollectionJobState.WaitingToRetry, retained.State);
    }

    [Fact]
    public async Task RateLimitBackoffIsSharedByOriginAndVerifiedUsageRefundsUnusedReservation()
    {
        var job = await jobs.EnqueueAsync(Definition(), "limited", default);
        var runner = Runner(new ReceiptCollector(429));
        var completed = await runner.ExecuteAsync(job.JobId, root, LeaseDuration);
        Assert.Equal(CollectionJobState.WaitingToRetry, completed.Job!.State);
        Assert.Equal(2, completed.Job.ReservedRequests);
        Assert.Equal(0, completed.Job.ReservedBytes);
        Assert.Equal(10, completed.Job.ReservedTimeoutSeconds);
        var other = await EnqueueClaimAsync("same-origin", Definition());
        var blocked = await jobs.TryStartAttemptAsync(other.Lease, root, default);
        Assert.Equal(CollectionJobBlockReason.OriginBackoff, blocked.BlockReason);
        Assert.True(blocked.RetryAt > DateTimeOffset.UtcNow.AddSeconds(50));
        var unrelated = await EnqueueClaimAsync("unrelated", Definition("https://elsewhere.test"));
        Assert.Equal(CollectionJobStartStatus.Started,
            (await jobs.TryStartAttemptAsync(unrelated.Lease, root, default)).Status);
    }

    [Fact]
    public async Task SavedReceiptRecoveryImportsWithoutCollectorAndCancellationPreservesEvidence()
    {
        var claim = await EnqueueClaimAsync("recover", Definition());
        var start = await jobs.TryStartAttemptAsync(claim.Lease, root, default);
        var receipt = ReceiptCollector.Result(start.Attempt!.Request, 404);
        var handoffs = new FileCollectionReceiptHandoffStore(root);
        await handoffs.SaveAsync(new PendingCollectionHandoff(1, start.Attempt.AttemptId, start.Attempt.Request, receipt), default);
        await jobs.ReleaseCollectorAsync(claim.Lease, start.CollectorLease!, default);
        await jobs.ReleaseClaimAsync(claim.Lease, default);
        await jobs.CancelAsync(claim.Job.JobId, default);

        var result = await Runner(new RejectCollector()).ExecuteAsync(claim.Job.JobId, root, LeaseDuration);

        Assert.Equal(CollectionJobState.Cancelled, result.Job!.State);
        Assert.Single(result.Job.Attempts);
        Assert.NotNull(await evidence.GetAsync(start.Attempt.AttemptId, default));
        Assert.Empty(await handoffs.ListAsync(default));
    }

    [Fact]
    public async Task AlreadyImportedReceiptWithoutSpoolRetainsReservationAndServerBackoff()
    {
        var claim = await EnqueueClaimAsync("already-imported", Definition());
        var start = await jobs.TryStartAttemptAsync(claim.Lease, root, default);
        await new CollectAndImportCollectionAttempt(new ReceiptCollector(429), evidence,
            new FileCollectionReceiptHandoffStore(root)).ExecuteAsync(start.Attempt!.AttemptId, start.Attempt.Request);
        await jobs.ReleaseCollectorAsync(claim.Lease, start.CollectorLease!, default);
        await jobs.ReleaseClaimAsync(claim.Lease, default);

        var result = await Runner(new RejectCollector()).ExecuteAsync(claim.Job.JobId, root, LeaseDuration);

        Assert.Equal(CollectionJobState.WaitingToRetry, result.Job!.State);
        Assert.Equal(5, result.Job.ReservedRequests);
        Assert.Equal(100, result.Job.ReservedBytes);
        var other = await EnqueueClaimAsync("after-recovery", Definition());
        Assert.Equal(CollectionJobBlockReason.OriginBackoff,
            (await jobs.TryStartAttemptAsync(other.Lease, root, default)).BlockReason);
    }

    [Fact]
    public async Task UnknownUsageCannotResetAggregateBudgetAndCancellationPreventsAdmission()
    {
        var definition = Definition() with { Policy = new CollectionJobPolicy { MaxAttempts = 3, MaxTotalRequests = 5, InitialRetryDelaySeconds = 0 } };
        var claim = await EnqueueClaimAsync("budget", definition);
        var start = await jobs.TryStartAttemptAsync(claim.Lease, root, default);
        await jobs.ReleaseCollectorAsync(claim.Lease, start.CollectorLease!, default);
        Assert.True(await jobs.SettleAttemptAsync(claim.Lease, start.Attempt!.AttemptId,
            new CollectionAttemptResolution(CollectionJobAttemptOutcome.Interrupted, null, "UnknownUsage"), default));
        Assert.Equal(CollectionJobState.Failed, (await jobs.GetAsync(claim.Job.JobId, default))!.State);
        Assert.Equal(CollectionJobStartStatus.Exhausted, (await jobs.TryStartAttemptAsync(claim.Lease, root, default)).Status);
        var cancelled = await EnqueueClaimAsync("cancel", Definition());
        await jobs.CancelAsync(cancelled.Job.JobId, default);
        Assert.True((await jobs.RenewAsync(cancelled.Lease, null, LeaseDuration, default)).CancellationRequested);
        Assert.Equal(CollectionJobStartStatus.Cancelled, (await jobs.TryStartAttemptAsync(cancelled.Lease, root, default)).Status);
    }

    private RunCollectionJob Runner(ICollectorProcess collector) => new(jobs, evidence,
        new FileCollectionReceiptHandoffStore(root), new CaptureArtifactVerifier(), collector);

    private async Task<CollectionJobClaim> EnqueueClaimAsync(string key, CollectionJobDefinition definition)
    {
        var job = await jobs.EnqueueAsync(definition, key, default);
        return (await jobs.TryClaimAsync(job.JobId, LeaseDuration, default))!;
    }

    private async Task SqlAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static CollectionJobDefinition Definition(string origin = "https://example.test") => new()
    {
        SourceId = "source",
        Url = origin + "/page",
        AllowedOrigin = origin,
        AllowedPathPrefix = "/page",
        MaxRequests = 5,
        MaxBytes = 100,
        TimeoutSeconds = 10,
        MinDelayMilliseconds = 0,
        Policy = new CollectionJobPolicy { InitialRetryDelaySeconds = 0 }
    };

    private sealed class RejectCollector : ICollectorProcess
    {
        public Task<CollectionResult> RunAsync(CollectionRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Recovery must not collect.");
    }

    private sealed class ReceiptCollector(int status) : ICollectorProcess
    {
        public Task<CollectionResult> RunAsync(CollectionRequest request, CancellationToken cancellationToken) => Task.FromResult(Result(request, status));
        public static CollectionResult Result(CollectionRequest request, int status) => new()
        {
            JobId = request.JobId,
            SourceId = request.SourceId,
            RequestedUrl = request.Url,
            FinalUrl = request.Url,
            Outcome = status == 429 ? CollectionOutcome.Deferred : CollectionOutcome.Failed,
            ObservedAt = DateTimeOffset.UtcNow,
            RequestCount = 2,
            BytesReceived = 0,
            Response = new HttpResponseMetadata { StatusCode = status, ContentEncodings = [] },
            FailureCode = status == 429 ? CollectionFailureCode.RateLimited : CollectionFailureCode.HttpError,
            RetryAfterSeconds = status == 429 ? 60 : null
        };
    }
}
