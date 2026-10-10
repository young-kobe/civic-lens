using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task LegacySettlementReplayWithoutNewReceiptFieldsRemainsIdempotent(int version)
    {
        var claim = await EnqueueClaimAsync($"v{version}-settlement", Definition());
        var start = await jobs.TryStartAttemptAsync(claim.Lease, root, default);
        var request = start.Attempt!.Request with { Version = version };
        var receipt = ReceiptCollector.Result(request, 404) with
        {
            Version = version,
            RobotsRequestCount = null,
            RobotsCrawlDelayMilliseconds = null
        };
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand("UPDATE collection_job_attempts SET request_json = @request WHERE attempt_id = @id", connection))
        {
            command.Parameters.AddWithValue("request", JsonSerializer.Serialize(request, CollectionProtocol.JsonOptions));
            command.Parameters.AddWithValue("id", start.Attempt.AttemptId);
            await command.ExecuteNonQueryAsync();
        }
        await CollectionAttemptImporter.ImportAsync(start.Attempt.AttemptId, request, receipt, evidence, default);
        var resolution = CollectionJobLifecycle.Resolve(receipt);
        Assert.True(await jobs.SettleAttemptAsync(claim.Lease, start.Attempt.AttemptId, resolution, default));
        var legacyJson = JsonSerializer.SerializeToNode(resolution, CollectionProtocol.JsonOptions)!;
        legacyJson["receipt"]!.AsObject().Remove("discovery");
        legacyJson["receipt"]!.AsObject().Remove("robotsRequestCount");
        legacyJson["receipt"]!.AsObject().Remove("robotsCrawlDelayMilliseconds");
        legacyJson.AsObject().Remove("robotsCrawlDelayMilliseconds");
        await using (var command = new NpgsqlCommand("UPDATE collection_job_attempts SET resolution_json = @resolution WHERE attempt_id = @id", connection))
        {
            command.Parameters.AddWithValue("resolution", legacyJson.ToJsonString());
            command.Parameters.AddWithValue("id", start.Attempt.AttemptId);
            await command.ExecuteNonQueryAsync();
        }
        Assert.True(await jobs.SettleAttemptAsync(claim.Lease, start.Attempt.AttemptId, resolution, default));
        Assert.Equal(2, (await jobs.GetAsync(claim.Job.JobId, default))!.ReservedRequests);
        await Assert.ThrowsAsync<InvalidOperationException>(() => jobs.SettleAttemptAsync(claim.Lease,
            start.Attempt.AttemptId, resolution with { Receipt = receipt with { FinalUrl = receipt.FinalUrl + "/changed" } }, default));
    }

    [Fact]
    public async Task SettlementWorksWithOnePooledConnection()
    {
        var pooled = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = true,
            MaxPoolSize = 1,
            Timeout = 3
        }.ConnectionString;
        jobs = PostgresCollectionJobStore.FromConnectionString(pooled);
        var claim = await EnqueueClaimAsync("single-connection", Definition());
        var start = await jobs.TryStartAttemptAsync(claim.Lease, root, default);
        var receipt = ReceiptCollector.Result(start.Attempt!.Request, 404);
        await CollectionAttemptImporter.ImportAsync(start.Attempt.AttemptId, start.Attempt.Request, receipt, evidence, default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.True(await jobs.SettleAttemptAsync(claim.Lease, start.Attempt.AttemptId,
            CollectionJobLifecycle.Resolve(receipt), timeout.Token));
        Assert.True((await jobs.RenewAsync(claim.Lease, start.CollectorLease, LeaseDuration, timeout.Token)).Owned);
        Assert.Equal(CollectionJobState.Failed, (await jobs.GetAsync(claim.Job.JobId, timeout.Token))!.State);
    }

    [Fact]
    public async Task ConflictingHandoffSettlesRetainedEvidenceWithoutRefundOrDeletion()
    {
        var claim = await EnqueueClaimAsync("conflicting-handoff", Definition());
        var start = await jobs.TryStartAttemptAsync(claim.Lease, root, default);
        var receipt = ReceiptCollector.Result(start.Attempt!.Request, 404);
        await CollectionAttemptImporter.ImportAsync(start.Attempt.AttemptId, start.Attempt.Request, receipt, evidence, default);
        var handoffs = new FileCollectionReceiptHandoffStore(root);
        await handoffs.SaveAsync(new PendingCollectionHandoff(1, start.Attempt.AttemptId, start.Attempt.Request,
            receipt with { FinalUrl = receipt.FinalUrl + "/different" }), default);
        await jobs.ReleaseCollectorAsync(claim.Lease, start.CollectorLease!, default);
        await jobs.ReleaseClaimAsync(claim.Lease, default);

        var result = await Runner(new RejectCollector()).ExecuteAsync(claim.Job.JobId, root, LeaseDuration);

        Assert.Equal(CollectionJobState.Failed, result.Job!.State);
        Assert.Equal(5, result.Job.ReservedRequests);
        Assert.Equal(100, result.Job.ReservedBytes);
        Assert.Null(Assert.Single(result.Job.Attempts).Resolution!.Receipt);
        Assert.Equal(start.Attempt.AttemptId, Assert.Single(await handoffs.ListAsync(default)));
        Assert.Equal(receipt.FinalUrl, (await evidence.GetAsync(start.Attempt.AttemptId, default))!.AttemptResult.FinalUrl);
        var next = await EnqueueClaimAsync("next", Definition());
        Assert.Equal(CollectionJobStartStatus.Started, (await jobs.TryStartAttemptAsync(next.Lease, root, default)).Status);
    }

    [Fact]
    public async Task GetAndListReadJobAttemptsWithoutWaitingForCoordinationLock()
    {
        var first = await EnqueueClaimAsync("first", Definition());
        var initial = await jobs.TryStartAttemptAsync(first.Lease, root, default);
        await jobs.ReleaseCollectorAsync(first.Lease, initial.CollectorLease!, default);
        await jobs.SettleAttemptAsync(first.Lease, initial.Attempt!.AttemptId,
            new CollectionAttemptResolution(CollectionJobAttemptOutcome.Interrupted, null, "UnknownUsage"), default);
        var retry = await jobs.TryStartAttemptAsync(first.Lease, root, default);
        var empty = await jobs.EnqueueAsync(Definition(), "empty", default);
        var counter = new ReadCounter();
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString)
            .AddInterceptors(counter).Options;
        var reader = new PostgresCollectionJobStore(new PooledDbContextFactory<CollectionAttemptDbContext>(options));

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended('civic-lens-collection-jobs', 0))", connection, transaction);
        await command.ExecuteNonQueryAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var listed = await reader.ListAsync(100, timeout.Token);
        var fetched = await reader.GetAsync(first.Job.JobId, timeout.Token);

        Assert.Equal(4, counter.Count);
        Assert.Equal(new[] { empty.JobId, first.Job.JobId }, listed.Select(job => job.JobId));
        Assert.Empty(listed[0].Attempts);
        Assert.Equal(new[] { initial.Attempt.AttemptId, retry.Attempt!.AttemptId },
            listed[1].Attempts.Select(attempt => attempt.AttemptId));
        Assert.NotNull(listed[1].Attempts[0].Resolution);
        Assert.Null(listed[1].Attempts[1].Resolution);
        Assert.NotNull(fetched);
        Assert.Equal(first.Job.JobId, fetched.JobId);
        Assert.Equal(CollectionJobState.Running, fetched.State);
        Assert.Equal(new[] { initial.Attempt.AttemptId, retry.Attempt.AttemptId },
            fetched.Attempts.Select(attempt => attempt.AttemptId));
        Assert.NotNull(fetched.Attempts[0].Resolution);
        Assert.Null(fetched.Attempts[1].Resolution);
        var limited = Assert.Single(await reader.ListAsync(1, timeout.Token));
        Assert.Equal(empty.JobId, limited.JobId);
        Assert.Empty(limited.Attempts);
    }

    private sealed class ReadCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
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
    public async Task SuccessfulCaptureRecoveryWithoutSpoolPreservesRobotsDelay()
    {
        var claim = await EnqueueClaimAsync("robots-delay-capture-recovery", Definition());
        var start = await jobs.TryStartAttemptAsync(claim.Lease, root, default);
        var hash = new string('a', 64);
        var receipt = ReceiptCollector.Result(start.Attempt!.Request, 404) with
        {
            Outcome = CollectionOutcome.Captured,
            Response = new HttpResponseMetadata { StatusCode = 200, ContentEncodings = [] },
            FailureCode = null,
            Capture = new CaptureArtifact { Sha256 = hash, RelativePath = hash + ".gz", ByteLength = 10 },
            BytesReceived = 10,
            RobotsCrawlDelayMilliseconds = 45_000
        };
        await CollectionAttemptImporter.ImportAsync(start.Attempt.AttemptId, start.Attempt.Request, receipt, evidence, default);
        await jobs.ReleaseCollectorAsync(claim.Lease, start.CollectorLease!, default);
        await jobs.ReleaseClaimAsync(claim.Lease, default);

        var recovered = await Runner(new RejectCollector()).ExecuteAsync(claim.Job.JobId, root, LeaseDuration);

        Assert.Equal(CollectionJobState.Succeeded, recovered.Job!.State);
        var other = await EnqueueClaimAsync("robots-delay-capture-same-origin", Definition());
        var blocked = await jobs.TryStartAttemptAsync(other.Lease, root, default);
        Assert.Equal(CollectionJobBlockReason.OriginBackoff, blocked.BlockReason);
        Assert.True(blocked.RetryAt > DateTimeOffset.UtcNow.AddSeconds(40));
    }

    [Fact]
    public async Task RobotsRateLimitWithoutContentRequestSharesRetryAfterBackoff()
    {
        var job = await jobs.EnqueueAsync(Definition(), "robots-rate-limit", default);
        var completed = await Runner(new RobotsRateLimitedCollector()).ExecuteAsync(job.JobId, root, LeaseDuration);
        Assert.Equal(CollectionJobState.WaitingToRetry, completed.Job!.State);

        var other = await EnqueueClaimAsync("robots-rate-limit-same-origin", Definition());
        var blocked = await jobs.TryStartAttemptAsync(other.Lease, root, default);
        Assert.Equal(CollectionJobBlockReason.OriginBackoff, blocked.BlockReason);
        Assert.True(blocked.RetryAt > DateTimeOffset.UtcNow.AddSeconds(50));
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

    [Fact]
    public async Task LegacyEnqueueReplayRetainsUnknownProvenanceAndNewJobsRetainRevisions()
    {
        var configuration = new CollectionConfiguration
        {
            People = [new PersonConfiguration { Id = "person", Name = "Original" }],
            Sources = [new WatchedSourceConfiguration
            {
                Id = "source", PersonIds = ["person"], Url = "https://example.test/page",
                AllowedOrigin = "https://example.test", AllowedPathPrefix = "/"
            }]
        };
        var definition = CollectionJobDefinition.FromConfiguration(configuration, "source", new DateOnly(2026, 7, 1));
        var legacy = await jobs.EnqueueAsync(definition with { ConfigurationRevision = null, CoverageAsOf = null }, "legacy", default);
        var replay = await jobs.EnqueueAsync(definition, "legacy", default);
        Assert.Equal(legacy.JobId, replay.JobId);
        Assert.Null(replay.Definition.ConfigurationRevision);
        var current = await jobs.EnqueueAsync(definition, "current", default);
        var reopened = PostgresCollectionJobStore.FromConnectionString(connectionString);
        var stored = await reopened.GetAsync(current.JobId, default);
        Assert.Equal(definition, stored!.Definition);
        configuration.People[0] = configuration.People[0] with { Name = "Correction" };
        var corrected = CollectionJobDefinition.FromConfiguration(configuration, "source", definition.CoverageAsOf);
        await Assert.ThrowsAsync<ArgumentException>(() => reopened.EnqueueAsync(corrected, "current", default));
        await Assert.ThrowsAsync<ArgumentException>(() => reopened.EnqueueAsync(definition with { ConfigurationRevision = null, CoverageAsOf = null }, "current", default));

        var source = new ConfiguredCollectionSource(configuration, "source");
        var concurrent = await Task.WhenAll(jobs.EnqueueAsync(source, "concurrent-configured", default),
            reopened.EnqueueAsync(source, "concurrent-configured", default));
        Assert.Equal(concurrent[0].JobId, concurrent[1].JobId);
        Assert.Equal(concurrent[0].Definition, concurrent[1].Definition);
        Assert.NotNull(concurrent[0].Definition.CoverageAsOf);

        var expiredConfiguration = configuration with
        {
            Version = 2,
            Sources = [configuration.Sources[0] with
            {
                PersonIds = null, Enabled = false,
                Coverage = [new SourceCoverageConfiguration { PersonId = "person", EndsBefore = new DateOnly(2021, 1, 1) }]
            }]
        };
        var expired = new ConfiguredCollectionSource(expiredConfiguration, "source");
        var legacyReplay = await reopened.EnqueueAsync(expired, "legacy", default);
        Assert.Equal(legacy.JobId, legacyReplay.JobId);
        Assert.Null(legacyReplay.Definition.ConfigurationRevision);
        await Assert.ThrowsAsync<ArgumentException>(() => reopened.EnqueueAsync(expired, "new-expired", default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StoredRevisionBindingIsRevalidatedBeforeExecution(bool removeDate)
    {
        var configuration = new CollectionConfiguration
        {
            People = [new PersonConfiguration { Id = "person", Name = "Person" }],
            Sources = [new WatchedSourceConfiguration
            {
                Id = "source", PersonIds = ["person"], Url = "https://example.test/page",
                AllowedOrigin = "https://example.test", AllowedPathPrefix = "/"
            }]
        };
        var definition = CollectionJobDefinition.FromConfiguration(configuration, "source", new DateOnly(2026, 7, 1));
        var claim = await EnqueueClaimAsync("corrupt-binding", definition);
        var invalid = removeDate ? definition with { CoverageAsOf = null } : definition with { MaxBytes = 100 };
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var update = new NpgsqlCommand("UPDATE collection_jobs SET definition_json = @definition WHERE job_id = @id", connection))
        {
            update.Parameters.AddWithValue("definition", JsonSerializer.Serialize(invalid, CollectionProtocol.JsonOptions));
            update.Parameters.AddWithValue("id", claim.Job.JobId);
            await update.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => jobs.GetAsync(claim.Job.JobId, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => jobs.TryStartAttemptAsync(claim.Lease, root, default));
        await using var count = new NpgsqlCommand("SELECT count(*) FROM collection_job_attempts WHERE job_id = @id", connection);
        count.Parameters.AddWithValue("id", claim.Job.JobId);
        Assert.Equal(0L, await count.ExecuteScalarAsync());
    }

    [Fact]
    public async Task JobPagesWalkOlderThenNewerOverTheSameRowsEvenWhenCreationTimesTie()
    {
        var ids = new List<string>();
        for (var index = 0; index < 7; index++) ids.Add((await jobs.EnqueueAsync(Definition(), "page-" + index, default)).JobId);
        // Three jobs share each creation time, so only the job ID tie-breaker (ascending) separates them.
        var keyed = ids.Select((id, index) => (Id: id, Ticks: 5_000L + index / 3)).ToArray();
        foreach (var key in keyed) await SqlAsync($"UPDATE collection_jobs SET created_at = {key.Ticks} WHERE job_id = '{key.Id}'");
        var expected = keyed.OrderByDescending(key => key.Ticks).ThenBy(key => key.Id, StringComparer.Ordinal)
            .Select(key => key.Id).ToArray();

        var walk = await PageWalk.RunAsync(async cursor =>
        {
            var page = await jobs.ListPageAsync(cursor, 2, default);
            return (page.Items.Select(job => job.JobId).ToArray(), page.NewerCursor, page.OlderCursor);
        });

        walk.AssertExact(expected);
    }

    [Fact]
    public async Task JobPageRejectsLimitOutsideBounds()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => jobs.ListPageAsync(null, 0, default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => jobs.ListPageAsync(null, 101, default));
    }

    [Fact]
    public async Task LatestBySourceReturnsTheNewestJobForTheExactSourceAndUrlOnly()
    {
        var older = await jobs.EnqueueAsync(Definition() with { SourceId = "a" }, "a-old", default);
        var newer = await jobs.EnqueueAsync(Definition() with { SourceId = "a" }, "a-new", default);
        var article = await jobs.EnqueueAsync(Definition() with { SourceId = "a", Url = "https://example.test/page/article" }, "a-article", default);
        var other = await jobs.EnqueueAsync(Definition() with { SourceId = "b" }, "b-only", default);
        await SqlAsync($"UPDATE collection_jobs SET created_at = 100 WHERE job_id = '{older.JobId}'");
        await SqlAsync($"UPDATE collection_jobs SET created_at = 200 WHERE job_id = '{newer.JobId}'");
        await SqlAsync($"UPDATE collection_jobs SET created_at = 300 WHERE job_id = '{article.JobId}'");
        await SqlAsync($"UPDATE collection_jobs SET created_at = 50 WHERE job_id = '{other.JobId}'");

        var latest = await jobs.ListLatestBySourceAsync([
            new("a", "https://example.test/page"), new("b", "https://example.test/page"), new("c", "https://example.test/page")], default);

        Assert.Equal(new[] { newer.JobId, other.JobId }.Order(), latest.Select(job => job.JobId).Order());
        Assert.Empty(await jobs.ListLatestBySourceAsync([], default));
    }

    [Fact]
    public async Task ActivityListsNewestStartsAndFailuresByFailureTime()
    {
        var oldFailure = await jobs.EnqueueAsync(Definition() with { SourceId = "old-failure" }, "old-failure", default);
        var lateFailure = await jobs.EnqueueAsync(Definition() with { SourceId = "late-failure" }, "late-failure", default);
        var running = await jobs.EnqueueAsync(Definition() with { SourceId = "fine" }, "fine", default);
        await SqlAsync($"UPDATE collection_jobs SET created_at = 100, state = 'Failed' WHERE job_id = '{oldFailure.JobId}'");
        await SqlAsync($"UPDATE collection_jobs SET created_at = 50, state = 'Failed' WHERE job_id = '{lateFailure.JobId}'");
        await SqlAsync($"UPDATE collection_jobs SET created_at = 200 WHERE job_id = '{running.JobId}'");
        // The job created first failed last: failure time, not creation time, orders failures.
        await SqlAsync($"INSERT INTO collection_job_attempts (attempt_id, job_id, sequence, request_json, started_at, resolution_json, completed_at) " +
            $"VALUES ('late-attempt', '{lateFailure.JobId}', 1, '{{}}', 60, '{{}}', 900)");

        var activity = await jobs.ListActivityAsync(2, default);

        Assert.Equal(["fine", "old-failure"], activity.Started.Select(item => item.SourceId));
        Assert.Equal(new DateTimeOffset(200, TimeSpan.Zero), activity.Started[0].OccurredAt);
        Assert.Equal(["late-failure", "old-failure"], activity.Failed.Select(item => item.SourceId));
        Assert.Equal(new DateTimeOffset(900, TimeSpan.Zero), activity.Failed[0].OccurredAt);
        Assert.Equal(new DateTimeOffset(100, TimeSpan.Zero), activity.Failed[1].OccurredAt);
    }

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

    private sealed class RobotsRateLimitedCollector : ICollectorProcess
    {
        public Task<CollectionResult> RunAsync(CollectionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new CollectionResult
            {
                JobId = request.JobId,
                SourceId = request.SourceId,
                RequestedUrl = request.Url,
                FinalUrl = request.Url,
                Outcome = CollectionOutcome.Deferred,
                ObservedAt = DateTimeOffset.UtcNow,
                RequestCount = 1,
                RobotsRequestCount = 1,
                BytesReceived = 0,
                FailureCode = CollectionFailureCode.RateLimited,
                RetryAfterSeconds = 60
            });
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
            RetryAfterSeconds = status == 429 ? 60 : null,
            RobotsRequestCount = request.Version >= 6 ? 1 : null
        };
    }
}
