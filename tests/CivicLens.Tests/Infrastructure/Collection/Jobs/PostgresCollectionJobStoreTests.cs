using System.Data.Common;
using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Tests.Application.Collection.Bills;
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
    private readonly string root = Path.Combine(Path.GetTempPath(), "civic-jobs-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PostgresCollectionJobStore jobs = null!;
    private PostgresCollectionAttemptStore evidence = null!;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        jobs = PostgresCollectionJobStore.FromConnectionString(connectionString);
        evidence = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        Directory.CreateDirectory(root);
    }

    public async Task DisposeAsync()
    {
        Directory.Delete(root, true);
        await postgres.DropDatabaseAsync(connectionString);
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

        await using var holder = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-collection-jobs");
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
        Assert.True(await UnclaimableUntilAsync("same-origin", Definition()) > DateTimeOffset.UtcNow.AddSeconds(50));
        var unrelated = await EnqueueClaimAsync("unrelated", Definition("https://elsewhere.test"));
        Assert.Equal(CollectionJobStartStatus.Started,
            (await jobs.TryStartAttemptAsync(unrelated.Lease, root, default)).Status);
    }

    [Fact]
    public async Task DerivedChargesMatchTheRetiredCountersAcrossStartRefundAndRetry()
    {
        var claim = await EnqueueClaimAsync("derived", Definition());
        var counters = new CollectionJobCharges(0, 0, 0);

        var first = await jobs.TryStartAttemptAsync(claim.Lease, root, default);
        counters = Reserve(counters, first.Attempt!.Request);
        Assert.Equal(counters, await ChargesAsync(claim.Job.JobId));

        var receipt = ReceiptCollector.Result(first.Attempt.Request, 429);
        await CollectionAttemptImporter.ImportAsync(first.Attempt.AttemptId, first.Attempt.Request, receipt, evidence, default);
        await jobs.ReleaseCollectorAsync(claim.Lease, first.CollectorLease!, default);
        Assert.True(await jobs.SettleAttemptAsync(claim.Lease, first.Attempt.AttemptId, CollectionJobLifecycle.Resolve(receipt), default));
        counters = counters with
        {
            Requests = counters.Requests - (first.Attempt.Request.MaxRequests - receipt.RequestCount),
            Bytes = counters.Bytes - (first.Attempt.Request.MaxBytes - receipt.BytesReceived)
        };
        Assert.Equal(counters, await ChargesAsync(claim.Job.JobId));

        await SqlAsync("UPDATE collection_jobs SET retry_at = 1; UPDATE collection_job_origins SET not_before = 0;");
        var second = await jobs.TryStartAttemptAsync(claim.Lease, root, default);
        Assert.Equal(CollectionJobStartStatus.Started, second.Status);
        counters = Reserve(counters, second.Attempt!.Request);
        Assert.Equal(counters, await ChargesAsync(claim.Job.JobId));

        await jobs.ReleaseCollectorAsync(claim.Lease, second.CollectorLease!, default);
        Assert.True(await jobs.SettleAttemptAsync(claim.Lease, second.Attempt.AttemptId,
            new CollectionAttemptResolution(CollectionJobAttemptOutcome.Interrupted, null, "NoReceipt"), default));
        Assert.Equal(new CollectionJobCharges(7, 100, 20), counters);
        Assert.Equal(counters, await ChargesAsync(claim.Job.JobId));

        async Task<CollectionJobCharges> ChargesAsync(string jobId)
        {
            var job = (await jobs.GetAsync(jobId, default))!;
            return new(job.ReservedRequests, job.ReservedBytes, job.ReservedTimeoutSeconds);
        }

        static CollectionJobCharges Reserve(CollectionJobCharges charged, CollectionRequest request) => new(
            charged.Requests + request.MaxRequests, charged.Bytes + request.MaxBytes, charged.TimeoutSeconds + request.TimeoutSeconds);
    }

    [Fact]
    public async Task RateLimitOnOneGroupMemberDelaysAnotherMemberOfTheSameGroup()
    {
        var limited = await jobs.EnqueueAsync(GroupedDefinition("a.house.gov"), "group-limited", default);
        await Runner(new RobotsDeferredCollector(CollectionFailureCode.RateLimited)).ExecuteAsync(limited.JobId, root, LeaseDuration);

        Assert.True(await UnclaimableUntilAsync("group-sibling", GroupedDefinition("b.house.gov")) >
            DateTimeOffset.UtcNow.AddSeconds(50));
        var otherGroup = await EnqueueClaimAsync("group-other", GroupedDefinition("a.senate.gov"));
        Assert.Equal(CollectionJobStartStatus.Started, (await jobs.TryStartAttemptAsync(otherGroup.Lease, root, default)).Status);
    }

    [Fact]
    public async Task ServerErrorOnOneGroupMemberOnlyAppliesTheNormalGroupDelay()
    {
        var failed = await jobs.EnqueueAsync(GroupedDefinition("a.house.gov"), "group-server-error", default);
        await Runner(new RobotsDeferredCollector(CollectionFailureCode.RobotsUnavailable)).ExecuteAsync(failed.JobId, root, LeaseDuration);

        var unclaimableUntil = await UnclaimableUntilAsync("group-after-error", GroupedDefinition("b.house.gov"));

        Assert.InRange(unclaimableUntil, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(10));
    }

    [Fact]
    public async Task EverySettlementSpacesGroupMembersByTheGroupDelay()
    {
        var first = await jobs.EnqueueAsync(GroupedDefinition("a.house.gov"), "group-spacing", default);
        await Runner(new ReceiptCollector(404)).ExecuteAsync(first.JobId, root, LeaseDuration);

        var unclaimableUntil = await UnclaimableUntilAsync("group-spacing-sibling", GroupedDefinition("b.house.gov"));

        Assert.InRange(unclaimableUntil, DateTimeOffset.UtcNow.AddSeconds(1), DateTimeOffset.UtcNow.AddSeconds(4));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM collection_job_origins WHERE origin = 'group:house.gov'"));
    }

    [Fact]
    public async Task LookalikeHostOutsideTheGroupIsNotDelayed()
    {
        var limited = await jobs.EnqueueAsync(GroupedDefinition("a.house.gov"), "lookalike-limited", default);
        await Runner(new RobotsDeferredCollector(CollectionFailureCode.RateLimited)).ExecuteAsync(limited.JobId, root, LeaseDuration);

        var lookalike = await EnqueueClaimAsync("lookalike", GroupedDefinition("evilhouse.gov"));

        Assert.Equal(CollectionJobStartStatus.Started, (await jobs.TryStartAttemptAsync(lookalike.Lease, root, default)).Status);
    }

    [Fact]
    public async Task AClosedGroupLeavesItsJobsUnclaimedWhileAnotherGroupInTheSameQueueIsClaimed()
    {
        var paced = await jobs.EnqueueAsync(GroupedDefinition("b.house.gov"), "closed-group", default);
        var open = await jobs.EnqueueAsync(GroupedDefinition("a.senate.gov"), "open-group", default);
        await ClosePacingKeyAsync("group:house.gov", TimeSpan.FromHours(1));

        var page = await PostgresCollectionWorkerQueue.FromConnectionString(connectionString)
            .GetEligibleJobIdsAsync(null, 10, default);

        Assert.Equal(open.JobId, Assert.Single(page.JobIds));
        Assert.Null(await jobs.TryClaimAsync(paced.JobId, LeaseDuration, default));
        Assert.NotNull(await jobs.TryClaimAsync(open.JobId, LeaseDuration, default));
    }

    [Fact]
    public async Task AJobBecomesClaimableOnceItsPacingBlockExpires()
    {
        var paced = await jobs.EnqueueAsync(GroupedDefinition("b.house.gov"), "expiring-group", default);
        await ClosePacingKeyAsync("group:house.gov", TimeSpan.FromHours(1));
        Assert.Null(await jobs.TryClaimAsync(paced.JobId, LeaseDuration, default));

        await ClosePacingKeyAsync("group:house.gov", TimeSpan.FromHours(-1));

        var page = await PostgresCollectionWorkerQueue.FromConnectionString(connectionString)
            .GetEligibleJobIdsAsync(null, 10, default);
        Assert.Equal(paced.JobId, Assert.Single(page.JobIds));
        Assert.NotNull(await jobs.TryClaimAsync(paced.JobId, LeaseDuration, default));
    }

    [Fact]
    public async Task RunningAJobWhoseGroupIsClosedReportsTheGroupBackoffInsteadOfNotClaimed()
    {
        var paced = await jobs.EnqueueAsync(GroupedDefinition("b.house.gov"), "run-closed-group", default);
        await ClosePacingKeyAsync("group:house.gov", TimeSpan.FromHours(1));
        var notBefore = (long)(await ScalarAsync("SELECT not_before FROM collection_job_origins WHERE origin = 'group:house.gov'"))!;

        var result = await Runner(new RejectCollector()).ExecuteAsync(paced.JobId, root, LeaseDuration);

        Assert.Equal(CollectionJobRunStatus.Blocked, result.Status);
        Assert.Equal(CollectionJobBlockReason.OriginBackoff, result.BlockReason);
        Assert.Equal(new DateTimeOffset(notBefore, TimeSpan.Zero), result.RetryAt);
        Assert.Equal(paced.JobId, result.Job!.JobId);
    }

    [Fact]
    public async Task RunningAJobThatAnotherWorkerHoldsIsStillNotClaimed()
    {
        var held = await EnqueueClaimAsync("run-held", Definition());

        var result = await Runner(new RejectCollector()).ExecuteAsync(held.Job.JobId, root, LeaseDuration);

        Assert.Equal(CollectionJobRunStatus.NotClaimed, result.Status);
        Assert.Null(result.BlockReason);
    }

    [Fact]
    public async Task ARunningJobIsClaimedForRecoveryEvenWhileItsPacingKeyIsClosed()
    {
        var claim = await EnqueueClaimAsync("recover-paced", GroupedDefinition("a.house.gov"));
        Assert.Equal(CollectionJobStartStatus.Started, (await jobs.TryStartAttemptAsync(claim.Lease, root, default)).Status);
        await SqlAsync("UPDATE collection_jobs SET lease_expires_at = 1; UPDATE collection_collector_slot SET expires_at = 1;");
        await ClosePacingKeyAsync("group:house.gov", TimeSpan.FromHours(1));

        Assert.NotNull(await jobs.TryClaimAsync(claim.Job.JobId, LeaseDuration, default));
    }

    [Fact]
    public async Task EachNewJobStoresTheKeyThatPacesItsAdmission()
    {
        var grouped = await jobs.EnqueueAsync(GroupedDefinition("a.house.gov"), "grouped-key", default);
        var ungrouped = await jobs.EnqueueAsync(Definition(), "origin-key", default);

        Assert.Equal("group:house.gov", await ScalarAsync($"SELECT pacing_key FROM collection_jobs WHERE job_id = '{grouped.JobId}'"));
        Assert.Equal("https://example.test:443",
            await ScalarAsync($"SELECT pacing_key FROM collection_jobs WHERE job_id = '{ungrouped.JobId}'"));
    }

    private Task ClosePacingKeyAsync(string key, TimeSpan remaining) => SqlAsync($"""
        INSERT INTO collection_job_origins (origin, not_before)
        VALUES ('{key}', {(DateTimeOffset.UtcNow + remaining).UtcTicks})
        ON CONFLICT (origin) DO UPDATE SET not_before = EXCLUDED.not_before
        """);

    private static CollectionJobDefinition GroupedDefinition(string host)
    {
        var configuration = new CollectionConfiguration
        {
            People = [new PersonConfiguration { Id = "person", Name = "Person" }],
            Sources = [new WatchedSourceConfiguration
            {
                Id = host, Coverage = [new SourceCoverageConfiguration { PersonId = "person" }],
                Url = $"https://{host}/page", AllowedOrigin = $"https://{host}", AllowedPathPrefix = "/page"
            }],
            PacingGroups =
            [
                new PacingGroupConfiguration { HostSuffix = "house.gov", MinDelayMilliseconds = 3000 },
                new PacingGroupConfiguration { HostSuffix = "senate.gov", MinDelayMilliseconds = 3000 }
            ]
        };
        return CollectionJobDefinition.FromConfiguration(configuration, host, new DateOnly(2026, 7, 1));
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
        Assert.True(await UnclaimableUntilAsync("after-recovery", Definition()) > DateTimeOffset.UtcNow);
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
        Assert.True(await UnclaimableUntilAsync("robots-delay-capture-same-origin", Definition()) >
            DateTimeOffset.UtcNow.AddSeconds(40));
    }

    [Fact]
    public async Task RobotsRateLimitWithoutContentRequestSharesRetryAfterBackoff()
    {
        var job = await jobs.EnqueueAsync(Definition(), "robots-rate-limit", default);
        var completed = await Runner(new RobotsRateLimitedCollector()).ExecuteAsync(job.JobId, root, LeaseDuration);
        Assert.Equal(CollectionJobState.WaitingToRetry, completed.Job!.State);

        Assert.True(await UnclaimableUntilAsync("robots-rate-limit-same-origin", Definition()) >
            DateTimeOffset.UtcNow.AddSeconds(50));
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

    [Fact]
    public async Task SettlingACapturedJobQueuesOnePreparationRowAndReplayDoesNotAddAnother()
    {
        var claim = await EnqueueClaimAsync("prepare-captured", Definition());
        var start = await jobs.TryStartAttemptAsync(claim.Lease, root, default);
        var receipt = await CapturedReceiptAsync(start.Attempt!.Request);
        await CollectionAttemptImporter.ImportAsync(start.Attempt.AttemptId, start.Attempt.Request, receipt, evidence, default);
        var resolution = CollectionJobLifecycle.Resolve(receipt);

        Assert.Equal(0L, await PreparationCountAsync(claim.Job.JobId));
        Assert.True(await jobs.SettleAttemptAsync(claim.Lease, start.Attempt.AttemptId, resolution, default));
        Assert.True(await jobs.SettleAttemptAsync(claim.Lease, start.Attempt.AttemptId, resolution, default));

        Assert.Equal(CollectionJobState.Succeeded, (await jobs.GetAsync(claim.Job.JobId, default))!.State);
        Assert.Equal(1L, await PreparationCountAsync(claim.Job.JobId));
        Assert.Equal(("source", "https://example.test/page", "Preparation", "Pending"), await PreparationValuesAsync(claim.Job.JobId));
    }

    [Fact]
    public async Task SettlingWithoutAReceiptQueuesOnePreparationRow()
    {
        var claim = await EnqueueClaimAsync("prepare-no-receipt", Definition());
        var start = await jobs.TryStartAttemptAsync(claim.Lease, root, default);
        var receipt = await CapturedReceiptAsync(start.Attempt!.Request);
        await CollectionAttemptImporter.ImportAsync(start.Attempt.AttemptId, start.Attempt.Request, receipt, evidence, default);
        var resolution = new CollectionAttemptResolution(CollectionJobAttemptOutcome.Succeeded, null, "EvidenceAlreadyImported");

        Assert.True(await jobs.SettleAttemptAsync(claim.Lease, start.Attempt.AttemptId, resolution, default));

        Assert.Equal(CollectionJobState.Succeeded, (await jobs.GetAsync(claim.Job.JobId, default))!.State);
        Assert.Equal(1L, await PreparationCountAsync(claim.Job.JobId));
    }

    [Fact]
    public async Task SuccessAfterARetryQueuesOnePreparationRowAndFailureQueuesNone()
    {
        var retry = await EnqueueClaimAsync("prepare-retry", Definition());
        var first = await jobs.TryStartAttemptAsync(retry.Lease, root, default);
        await jobs.ReleaseCollectorAsync(retry.Lease, first.CollectorLease!, default);
        Assert.True(await jobs.SettleAttemptAsync(retry.Lease, first.Attempt!.AttemptId,
            new CollectionAttemptResolution(CollectionJobAttemptOutcome.Interrupted, null, "UnknownUsage"), default));
        Assert.Equal(0L, await PreparationCountAsync(retry.Job.JobId));
        await SqlAsync($"UPDATE collection_jobs SET retry_at = 0 WHERE job_id = '{retry.Job.JobId}'");
        var second = await jobs.TryStartAttemptAsync(retry.Lease, root, default);
        var receipt = await CapturedReceiptAsync(second.Attempt!.Request);
        await CollectionAttemptImporter.ImportAsync(second.Attempt.AttemptId, second.Attempt.Request, receipt, evidence, default);
        Assert.True(await jobs.SettleAttemptAsync(retry.Lease, second.Attempt.AttemptId, CollectionJobLifecycle.Resolve(receipt), default));
        Assert.Equal(1L, await PreparationCountAsync(retry.Job.JobId));
        await jobs.ReleaseCollectorAsync(retry.Lease, second.CollectorLease!, default);
        await SqlAsync("UPDATE collection_job_origins SET not_before = 0");

        var failed = await EnqueueClaimAsync("prepare-failed", Definition());
        var start = await jobs.TryStartAttemptAsync(failed.Lease, root, default);
        var notFound = ReceiptCollector.Result(start.Attempt!.Request, 404);
        await CollectionAttemptImporter.ImportAsync(start.Attempt.AttemptId, start.Attempt.Request, notFound, evidence, default);
        Assert.True(await jobs.SettleAttemptAsync(failed.Lease, start.Attempt.AttemptId, CollectionJobLifecycle.Resolve(notFound), default));
        Assert.Equal(0L, await PreparationCountAsync(failed.Job.JobId));
    }

    [Fact]
    public async Task ExistingPreparationRowIsKeptWhenAJobSucceeds()
    {
        var claim = await EnqueueClaimAsync("prepare-existing", Definition());
        var start = await jobs.TryStartAttemptAsync(claim.Lease, root, default);
        var receipt = await CapturedReceiptAsync(start.Attempt!.Request);
        await CollectionAttemptImporter.ImportAsync(start.Attempt.AttemptId, start.Attempt.Request, receipt, evidence, default);
        await SqlAsync($"""
            INSERT INTO evidence_processing (job_id, attempt_id, source_id, requested_url, stage, status,
                fence, attempts, admitted_count, deferred_count, duplicate_count)
            VALUES ('{claim.Job.JobId}', 'prepare', 'source', 'https://example.test/page', 'Preparation', 'Pending', 0, 0, 0, 0, 0)
            """);

        Assert.True(await jobs.SettleAttemptAsync(claim.Lease, start.Attempt.AttemptId, CollectionJobLifecycle.Resolve(receipt), default));

        Assert.Equal(1L, await PreparationCountAsync(claim.Job.JobId));
    }

    [Fact]
    public async Task FailedPreparationInsertRollsBackTheSettlement()
    {
        var claim = await EnqueueClaimAsync("prepare-rollback", Definition());
        var start = await jobs.TryStartAttemptAsync(claim.Lease, root, default);
        var receipt = await CapturedReceiptAsync(start.Attempt!.Request);
        await CollectionAttemptImporter.ImportAsync(start.Attempt.AttemptId, start.Attempt.Request, receipt, evidence, default);
        await SqlAsync("""
            CREATE FUNCTION fail_preparation() RETURNS trigger AS $$ BEGIN RAISE EXCEPTION 'preparation refused'; END; $$ LANGUAGE plpgsql;
            CREATE TRIGGER trg_fail_preparation BEFORE INSERT ON evidence_processing
            FOR EACH ROW EXECUTE FUNCTION fail_preparation();
            """);

        await Assert.ThrowsAnyAsync<Exception>(() => jobs.SettleAttemptAsync(claim.Lease, start.Attempt.AttemptId,
            CollectionJobLifecycle.Resolve(receipt), default));

        Assert.Equal(CollectionJobState.Running, (await jobs.GetAsync(claim.Job.JobId, default))!.State);
        Assert.Equal(0L, await PreparationCountAsync(claim.Job.JobId));
        await SqlAsync("DROP TRIGGER trg_fail_preparation ON evidence_processing");
        Assert.True(await jobs.SettleAttemptAsync(claim.Lease, start.Attempt.AttemptId, CollectionJobLifecycle.Resolve(receipt), default));
        Assert.Equal(1L, await PreparationCountAsync(claim.Job.JobId));
    }

    [Fact]
    public async Task WorkflowTriggersAreGoneAndWakeupTriggersRemain()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM pg_proc WHERE proname IN
                        ('civic_lens_register_document_change_analysis', 'civic_lens_create_successful_job_preparation',
                         'civic_lens_release_predecessor_waiters')),
                   (SELECT count(*) FROM pg_trigger WHERE tgname IN
                        ('trg_document_comparisons_register_analysis', 'trg_collection_jobs_create_preparation',
                         'trg_evidence_processing_release_waiters', 'trg_collection_jobs_release_waiters')),
                   (SELECT count(*) FROM pg_proc WHERE proname IN
                        ('civic_lens_notify_work_change', 'civic_lens_notify_analysis_change'))
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal((0L, 0L, 2L), (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2)));
    }

    private async Task<CollectionResult> CapturedReceiptAsync(CollectionRequest request)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("Retained article text.\n");
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        await using (var file = File.Create(Path.Combine(root, hash + ".gz")))
        await using (var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionLevel.SmallestSize))
            await gzip.WriteAsync(bytes);
        return ReceiptCollector.Result(request, 200) with
        {
            Outcome = CollectionOutcome.Captured,
            BytesReceived = bytes.Length,
            Capture = new CaptureArtifact { Sha256 = hash, RelativePath = hash + ".gz", ByteLength = bytes.Length },
            Response = new HttpResponseMetadata { StatusCode = 200, ContentType = "text/plain; charset=utf-8", ContentEncodings = [] },
            FailureCode = null
        };
    }

    private async Task<long> PreparationCountAsync(string jobId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM evidence_processing WHERE job_id = @id", connection);
        command.Parameters.AddWithValue("id", jobId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<(string Source, string Url, string Stage, string Status)> PreparationValuesAsync(string jobId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT source_id, requested_url, stage, status FROM evidence_processing WHERE job_id = @id AND attempt_id = 'prepare'", connection);
        command.Parameters.AddWithValue("id", jobId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
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
                Id = "source", Coverage = [new SourceCoverageConfiguration { PersonId = "person" }], Url = "https://example.test/page",
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
                Enabled = false,
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
                Id = "source", Coverage = [new SourceCoverageConfiguration { PersonId = "person" }], Url = "https://example.test/page",
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
            update.Parameters.AddWithValue("definition",
                JsonSerializer.Serialize(invalid with { ConfigurationRevision = null }, CollectionProtocol.JsonOptions));
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
    public async Task AJobReadsBackTheExactRevisionAndTheRowStoresOnlyItsId()
    {
        var definition = RevisionDefinition();

        var job = await jobs.EnqueueAsync(definition, "revision-reference", default);

        var read = await jobs.GetAsync(job.JobId, default);
        Assert.Equal(definition.ConfigurationRevision, read!.Definition.ConfigurationRevision);
        Assert.Equal(definition, read.Definition);
        Assert.Equal(definition.ConfigurationRevision!.Id, await ScalarAsync("SELECT configuration_revision_id FROM collection_jobs"));
        Assert.Null(await ScalarAsync("SELECT definition_json::jsonb ->> 'configurationRevision' FROM collection_jobs"));
        Assert.Equal(definition.ConfigurationRevision.Json, await ScalarAsync("SELECT json FROM collection_configuration_revisions"));
    }

    [Fact]
    public async Task JobsAdmittedFromOneRevisionShareOneStoredRevision()
    {
        var definition = RevisionDefinition();

        await jobs.EnqueueAsync(definition, "shared-one", default);
        await jobs.EnqueueAsync(definition, "shared-two", default);

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM collection_configuration_revisions"));
        Assert.All(await jobs.ListAsync(10, default), job => Assert.Equal(definition.ConfigurationRevision, job.Definition.ConfigurationRevision));
    }

    [Fact]
    public async Task ATamperedStoredRevisionFailsTheHashCheckInsteadOfBindingTheJobToDifferentSettings()
    {
        var job = await jobs.EnqueueAsync(RevisionDefinition(), "tampered-revision", default);
        await ExecuteAsync("UPDATE collection_configuration_revisions SET json = json || ' '");

        await Assert.ThrowsAsync<InvalidDataException>(() => jobs.GetAsync(job.JobId, default));
    }

    [Fact]
    public async Task TheDatabaseRefusesToDeleteARevisionThatAJobStillNeeds()
    {
        var job = await jobs.EnqueueAsync(RevisionDefinition(), "referenced-revision", default);

        var refused = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("DELETE FROM collection_configuration_revisions"));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, refused.SqlState);
        Assert.NotNull((await jobs.GetAsync(job.JobId, default))!.Definition.ConfigurationRevision);
    }

    private static CollectionJobDefinition RevisionDefinition()
    {
        var configuration = new CollectionConfiguration
        {
            People = [new PersonConfiguration { Id = "person", Name = "Person" }],
            Sources = [new WatchedSourceConfiguration
            {
                Id = "source", Coverage = [new SourceCoverageConfiguration { PersonId = "person" }], Url = "https://example.test/page",
                AllowedOrigin = "https://example.test", AllowedPathPrefix = "/"
            }]
        };
        return CollectionJobDefinition.FromConfiguration(configuration, "source", new DateOnly(2026, 7, 1));
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
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

    // Source health shows the latest check of each source, so a newer article job must not hide it.
    [Fact]
    public async Task LatestBySourceReturnsTheNewestRootJobOfEachSourceOnly()
    {
        var configuration = new CollectionConfiguration
        {
            People = [new PersonConfiguration { Id = "person", Name = "Person" }],
            Sources = [.. new[] { "a", "b" }.Select(id => new WatchedSourceConfiguration
            {
                Id = id, Mode = CollectionMode.Html, Coverage = [new SourceCoverageConfiguration { PersonId = "person" }],
                Url = $"https://example.test/{id}", AllowedOrigin = "https://example.test", AllowedPathPrefix = "/"
            })]
        };
        var a = CollectionJobDefinition.FromConfiguration(configuration, "a");
        var older = await jobs.EnqueueAsync(a, "a-old", default);
        var newer = await jobs.EnqueueAsync(a, "a-new", default);
        var article = await jobs.EnqueueAsync(a with { Mode = CollectionMode.Page, Url = "https://example.test/a/article" },
            "a-article", default);
        var other = await jobs.EnqueueAsync(CollectionJobDefinition.FromConfiguration(configuration, "b"), "b-only", default);
        await SqlAsync($"UPDATE collection_jobs SET created_at = 100 WHERE job_id = '{older.JobId}'");
        await SqlAsync($"UPDATE collection_jobs SET created_at = 200 WHERE job_id = '{newer.JobId}'");
        await SqlAsync($"UPDATE collection_jobs SET created_at = 300 WHERE job_id = '{article.JobId}'");
        await SqlAsync($"UPDATE collection_jobs SET created_at = 50 WHERE job_id = '{other.JobId}'");

        var latest = await jobs.ListLatestBySourceAsync(["a", "b", "c"], default);

        Assert.Equal(new[] { newer.JobId, other.JobId }.Order(), latest.Select(job => job.JobId).Order());
        Assert.Empty(await jobs.ListLatestBySourceAsync([], default));
    }

    // A bill list URL carries its window, so it never equals the configured URL. Bill source health still needs its latest list job.
    [Fact]
    public async Task LatestBySourceFindsTheNewestBillListJobAndSkipsNewerDetailJobs()
    {
        var backfill = new DateOnly(2025, 1, 3);
        var configured = new ConfiguredCollectionSource(CongressBillSourceConfigurationTests.Configuration(), "congress-bills", backfill);
        var listUrl = new BillStreamWindow(backfill, backfill).RequestUrl(configured.SourceUrl);
        var list = await jobs.EnqueueAsync(configured.CreateApiJobDefinition(ApiResource.BillList, listUrl, backfill), "list", default);
        var detail = await jobs.EnqueueAsync(configured.CreateApiJobDefinition(ApiResource.Bill,
            "https://api.congress.gov/v3/bill/119/hr/1?format=json", backfill), "detail", default);
        await SqlAsync($"UPDATE collection_jobs SET created_at = 100 WHERE job_id = '{list.JobId}'");
        await SqlAsync($"UPDATE collection_jobs SET created_at = 200 WHERE job_id = '{detail.JobId}'");

        var latest = await jobs.ListLatestBySourceAsync(["congress-bills"], default);

        Assert.Equal(list.JobId, Assert.Single(latest).JobId);
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

    private async Task<DateTimeOffset> UnclaimableUntilAsync(string key, CollectionJobDefinition definition)
    {
        var job = await jobs.EnqueueAsync(definition, key, default);
        Assert.Null(await jobs.TryClaimAsync(job.JobId, LeaseDuration, default));
        var notBefore = (long)(await ScalarAsync($"""
            SELECT pace.not_before FROM collection_jobs job
              JOIN collection_job_origins pace ON pace.origin = job.pacing_key
             WHERE job.job_id = '{job.JobId}'
            """))!;
        return new DateTimeOffset(notBefore, TimeSpan.Zero);
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

    private sealed class RobotsDeferredCollector(CollectionFailureCode failureCode) : ICollectorProcess
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
                FailureCode = failureCode,
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
