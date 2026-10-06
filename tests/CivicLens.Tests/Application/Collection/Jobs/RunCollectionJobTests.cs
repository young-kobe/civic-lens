using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;

namespace CivicLens.Tests.Application.Collection.Jobs;

public sealed class RunCollectionJobTests
{
    [Fact]
    public async Task LostRenewalCancelsLongRecoveryAndPreventsStartingAnotherAttempt()
    {
        var request = Request();
        var pending = new CollectionJobAttempt("attempt-1", request, null, DateTimeOffset.UtcNow, null);
        var jobs = new FakeJobs(Job(pending)) { FailRenewals = true };
        var attempts = new BlockingAttemptStore();
        var runner = new RunCollectionJob(jobs, attempts, new EmptyHandoffs(), new NoopVerifier(), new CountingCollector());

        var result = await runner.ExecuteAsync("job-1", Path.GetTempPath(), TimeSpan.FromMilliseconds(300));

        Assert.Equal(CollectionJobRunStatus.LostOwnership, result.Status);
        Assert.True(attempts.Cancelled);
        Assert.Equal(0, jobs.StartCalls);
    }

    [Fact]
    public async Task RenewalExceptionCancelsActiveCollectorPromptly()
    {
        var request = Request();
        var jobs = new FakeJobs(PendingJob(request)) { FailRenewals = true, StartRequest = request };
        var collector = new BlockingCollector();
        var runner = new RunCollectionJob(jobs, new BlockingAttemptStore(), new EmptyHandoffs(), new NoopVerifier(), collector);

        var result = await runner.ExecuteAsync("job-1", Path.GetTempPath(), TimeSpan.FromMilliseconds(300));

        Assert.Equal(CollectionJobRunStatus.LostOwnership, result.Status);
        Assert.True(collector.CancellationObserved);
    }

    [Fact]
    public async Task PersistedCancellationStopsCollectorButSettlesReturnedReceipt()
    {
        var request = Request();
        var jobs = new FakeJobs(PendingJob(request)) { CancelOnRenewal = true, StartRequest = request };
        var handoffs = new TrackingHandoffs();
        var runner = new RunCollectionJob(jobs, new InMemoryAttemptStore(), handoffs,
            new NoopVerifier(), new ReturnReceiptOnCancelCollector());

        var result = await runner.ExecuteAsync("job-1", Path.GetTempPath(), TimeSpan.FromMilliseconds(300));

        Assert.Equal(CollectionJobRunStatus.Completed, result.Status);
        Assert.Equal(CollectionJobState.Cancelled, result.Job!.State);
        Assert.Equal(1, jobs.SettleCalls);
        Assert.True(handoffs.Deleted);
    }

    [Fact]
    public async Task PersistedCancellationWithoutReceiptSettlesInterruptedAttempt()
    {
        var request = Request();
        var jobs = new FakeJobs(PendingJob(request)) { CancelOnRenewal = true, StartRequest = request };
        var runner = new RunCollectionJob(jobs, new BlockingAttemptStore(), new EmptyHandoffs(),
            new NoopVerifier(), new BlockingCollector());

        var result = await runner.ExecuteAsync("job-1", Path.GetTempPath(), TimeSpan.FromMilliseconds(300));

        Assert.Equal(CollectionJobRunStatus.Cancelled, result.Status);
        Assert.Equal(CollectionJobState.Cancelled, result.Job!.State);
        var settled = Assert.Single(result.Job.Attempts);
        Assert.Equal(CollectionJobAttemptOutcome.Interrupted, settled.Resolution!.Outcome);
    }

    [Fact]
    public async Task UnconfirmedSettlementKeepsReceiptAndReturnsImportPending()
    {
        var request = Request();
        var jobs = new FakeJobs(PendingJob(request)) { ReturnFalseOnSettlement = true, StartRequest = request };
        var handoffs = new TrackingHandoffs();
        var runner = new RunCollectionJob(jobs, new InMemoryAttemptStore(), handoffs,
            new NoopVerifier(), new ReturnReceiptCollector());

        var result = await runner.ExecuteAsync("job-1", Path.GetTempPath(), TimeSpan.FromSeconds(5));

        Assert.Equal(CollectionJobRunStatus.ImportPending, result.Status);
        Assert.True(handoffs.Saved);
        Assert.False(handoffs.Deleted);
    }

    [Fact]
    public async Task ExistingEvidenceSettlesInterruptedAttemptWithoutFetchingAgain()
    {
        var request = Request();
        var sourceAttempt = new CollectionJobAttempt("attempt-1", request, null, DateTimeOffset.UtcNow, null);
        var job = Job(sourceAttempt);
        var jobs = new FakeJobs(job);
        var retained = new StoredCollectionAttempt(new CapturedAttemptResult("attempt-1", "source", request.Url,
            request.Url, DateTimeOffset.UtcNow, new CollectionResponse(200, null, null, "text/html", []),
            new CaptureIdentity(new string('a', 64), 4)), null, null);
        var attempts = new ExistingAttemptStore(retained);
        var collector = new CountingCollector();
        var runner = new RunCollectionJob(jobs, attempts, new EmptyHandoffs(), new NoopVerifier(), collector);

        var result = await runner.ExecuteAsync("job-1", Path.GetTempPath(), TimeSpan.FromSeconds(30));

        Assert.Equal(CollectionJobRunStatus.Reconciled, result.Status);
        Assert.Equal(CollectionJobState.Succeeded, result.Job!.State);
        Assert.Equal(0, collector.Calls);
        Assert.Equal(0, jobs.StartCalls);
        Assert.All(jobs.ReadJobIds, id => Assert.Equal("job-1", id));
    }

    private static CollectionRequest Request() => new()
    {
        JobId = "wire-request-1",
        SourceId = "source",
        Url = "https://example.test/page",
        AllowedOrigin = "https://example.test",
        AllowedPathPrefix = "/page",
        ArtifactDirectory = Path.GetFullPath("captures")
    };

    private static CollectionJobRecord Job(CollectionJobAttempt attempt)
    {
        var request = attempt.Request;
        var definition = new CollectionJobDefinition
        {
            SourceId = "source",
            Url = request.Url,
            AllowedOrigin = request.AllowedOrigin,
            AllowedPathPrefix = request.AllowedPathPrefix,
            MaxRequests = request.MaxRequests,
            MaxBytes = request.MaxBytes,
            TimeoutSeconds = request.TimeoutSeconds,
            MinDelayMilliseconds = request.MinDelayMilliseconds,
            Policy = new CollectionJobPolicy()
        };
        return new CollectionJobRecord("job-1", definition, "key", CollectionJobState.Running,
            DateTimeOffset.UtcNow, null, false, 5, 2_000_000, 30, [attempt]);
    }

    private static CollectionJobRecord PendingJob(CollectionRequest request)
    {
        var definition = new CollectionJobDefinition
        {
            SourceId = request.SourceId,
            Url = request.Url,
            AllowedOrigin = request.AllowedOrigin,
            AllowedPathPrefix = request.AllowedPathPrefix,
            MaxRequests = request.MaxRequests,
            MaxBytes = request.MaxBytes,
            TimeoutSeconds = request.TimeoutSeconds,
            MinDelayMilliseconds = request.MinDelayMilliseconds,
            Policy = new CollectionJobPolicy()
        };
        return new CollectionJobRecord("job-1", definition, "key", CollectionJobState.Pending,
            DateTimeOffset.UtcNow, null, false, 0, 0, 0, []);
    }

    private sealed class ExistingAttemptStore(StoredCollectionAttempt stored) : ICollectionAttemptStore
    {
        public Task<StoredCollectionAttempt?> GetAsync(string attemptId, CancellationToken cancellationToken) =>
            Task.FromResult<StoredCollectionAttempt?>(attemptId == stored.AttemptResult.AttemptId ? stored : null);
        public Task<CollectionImportDecision> ImportAtomicallyAsync(CollectionAttemptImport attempt,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class BlockingAttemptStore : ICollectionAttemptStore
    {
        public bool Cancelled { get; private set; }
        public async Task<StoredCollectionAttempt?> GetAsync(string attemptId, CancellationToken cancellationToken)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                throw;
            }
            return null;
        }
        public Task<CollectionImportDecision> ImportAtomicallyAsync(CollectionAttemptImport attempt, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeJobs(CollectionJobRecord job) : ICollectionJobStore
    {
        private CollectionJobRecord current = job;
        public int StartCalls { get; private set; }
        public int SettleCalls { get; private set; }
        public List<string> ReadJobIds { get; } = [];
        public bool FailRenewals { get; init; }
        public bool CancelOnRenewal { get; init; }
        public bool ReturnFalseOnSettlement { get; init; }
        public CollectionRequest? StartRequest { get; init; }
        public Task<CollectionJobRecord> EnqueueAsync(CollectionJobDefinition definition, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobRecord?> GetAsync(string jobId, CancellationToken cancellationToken)
        {
            ReadJobIds.Add(jobId);
            return Task.FromResult<CollectionJobRecord?>(current);
        }
        public Task<IReadOnlyList<CollectionJobRecord>> ListAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobRecord?> CancelAsync(string jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobClaim?> TryClaimAsync(string jobId, TimeSpan leaseDuration, CancellationToken cancellationToken) =>
            Task.FromResult<CollectionJobClaim?>(new CollectionJobClaim(new CollectionJobLease(jobId, "token", 1, DateTimeOffset.UtcNow.Add(leaseDuration)), current));
        public Task<CollectionJobRenewal> RenewAsync(CollectionJobLease jobLease, CollectionCollectorLease? collectorLease, TimeSpan leaseDuration, CancellationToken cancellationToken) =>
            Task.FromResult(FailRenewals
                ? new CollectionJobRenewal(false, false, null, null)
                : new CollectionJobRenewal(true, CancelOnRenewal,
                    jobLease with { ExpiresAt = DateTimeOffset.UtcNow.Add(leaseDuration) }, collectorLease));
        public Task<CollectionJobStartResult> TryStartAttemptAsync(CollectionJobLease jobLease, string artifactDirectory, CancellationToken cancellationToken)
        {
            StartCalls++;
            if (StartRequest is not null)
            {
                var attempt = new CollectionJobAttempt("attempt-new", StartRequest with { ArtifactDirectory = artifactDirectory },
                    null, DateTimeOffset.UtcNow, null);
                var collectorLease = new CollectionCollectorLease("collector", 1, DateTimeOffset.UtcNow.AddSeconds(1));
                return Task.FromResult(new CollectionJobStartResult(CollectionJobStartStatus.Started, attempt, collectorLease));
            }
            throw new InvalidOperationException("A terminal job cannot start another attempt.");
        }
        public Task<bool> ReleaseCollectorAsync(CollectionJobLease jobLease, CollectionCollectorLease collectorLease, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SettleAttemptAsync(CollectionJobLease jobLease, string attemptId, CollectionAttemptResolution resolution, CancellationToken cancellationToken)
        {
            SettleCalls++;
            if (ReturnFalseOnSettlement) return Task.FromResult(false);
            var settled = current.Attempts.Select(attempt => attempt.AttemptId == attemptId
                ? attempt with { Resolution = resolution, CompletedAt = DateTimeOffset.UtcNow } : attempt).ToList();
            if (settled.All(attempt => attempt.AttemptId != attemptId) && StartRequest is not null)
                settled.Add(new CollectionJobAttempt(attemptId, StartRequest, resolution, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            current = current with
            {
                State = CancelOnRenewal || current.CancellationRequested ? CollectionJobState.Cancelled : CollectionJobState.Succeeded,
                CancellationRequested = CancelOnRenewal || current.CancellationRequested,
                Attempts = settled
            };
            return Task.FromResult(true);
        }
        public Task<bool> ReleaseClaimAsync(CollectionJobLease jobLease, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class EmptyHandoffs : ICollectionReceiptHandoffStore
    {
        public ValueTask<IAsyncDisposable> AcquireRecoveryLeaseAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IAsyncDisposable>(new NoopLease());
        public Task SaveAsync(PendingCollectionHandoff handoff, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<PendingCollectionHandoff> LoadAsync(string handoffId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(string handoffId, CancellationToken cancellationToken) => throw new NotSupportedException();
        private sealed class NoopLease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }

    private sealed class TrackingHandoffs : ICollectionReceiptHandoffStore
    {
        private readonly Dictionary<string, PendingCollectionHandoff> entries = [];
        public bool Saved { get; private set; }
        public bool Deleted { get; private set; }
        public ValueTask<IAsyncDisposable> AcquireRecoveryLeaseAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IAsyncDisposable>(new NoopLease());
        public Task SaveAsync(PendingCollectionHandoff handoff, CancellationToken cancellationToken)
        {
            entries.Add(handoff.AttemptId, handoff);
            Saved = true;
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>(entries.Keys.ToArray());
        public Task<PendingCollectionHandoff> LoadAsync(string handoffId, CancellationToken cancellationToken) => Task.FromResult(entries[handoffId]);
        public Task DeleteAsync(string handoffId, CancellationToken cancellationToken)
        {
            entries.Remove(handoffId);
            Deleted = true;
            return Task.CompletedTask;
        }
        private sealed class NoopLease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }

    private sealed class InMemoryAttemptStore : ICollectionAttemptStore
    {
        private readonly Dictionary<string, StoredCollectionAttempt> retained = [];
        public Task<StoredCollectionAttempt?> GetAsync(string attemptId, CancellationToken cancellationToken) =>
            Task.FromResult(retained.GetValueOrDefault(attemptId));
        public Task<CollectionImportDecision> ImportAtomicallyAsync(CollectionAttemptImport attempt,
            CancellationToken cancellationToken)
        {
            retained.TryGetValue(attempt.AttemptResult.AttemptId, out var existing);
            var decision = attempt.DecideFromCandidates(existing,
                retained.Values.Select(value => value.AttemptResult).OfType<CapturedAttemptResult>());
            retained[attempt.AttemptResult.AttemptId] = new StoredCollectionAttempt(decision.AttemptResult,
                decision.SentValidators, decision.PriorCapturedAttempt);
            return Task.FromResult(decision);
        }
    }

    private sealed class NoopVerifier : ICaptureArtifactVerifier
    {
        public Task VerifyAsync(CollectionRequest request, CollectionResult receipt, string artifactRoot, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CountingCollector : ICollectorProcess
    {
        public int Calls { get; private set; }
        public Task<CollectionResult> RunAsync(CollectionRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("Recovery must not launch the collector.");
        }
    }

    private sealed class BlockingCollector : ICollectorProcess
    {
        public bool CancellationObserved { get; private set; }
        public async Task<CollectionResult> RunAsync(CollectionRequest request, CancellationToken cancellationToken)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
            throw new InvalidOperationException("Collector should have been cancelled.");
        }
    }

    private sealed class ReturnReceiptOnCancelCollector : ICollectorProcess
    {
        public async Task<CollectionResult> RunAsync(CollectionRequest request, CancellationToken cancellationToken)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { }
            return Receipt(request);
        }
    }

    private sealed class ReturnReceiptCollector : ICollectorProcess
    {
        public Task<CollectionResult> RunAsync(CollectionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Receipt(request));
    }

    private static CollectionResult Receipt(CollectionRequest request) => new()
    {
        JobId = request.JobId,
        SourceId = request.SourceId,
        RequestedUrl = request.Url,
        FinalUrl = request.Url,
        Outcome = CollectionOutcome.Failed,
        ObservedAt = DateTimeOffset.UtcNow,
        FailureCode = CollectionFailureCode.Timeout,
        BytesReceived = 0,
        RequestCount = 1
    };
}
