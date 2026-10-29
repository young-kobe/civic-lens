using Microsoft.Extensions.Logging.Abstractions;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Documents;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.Processing;

public sealed class EvidenceProcessingWorkerConcurrencyTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task ItemsRunAtOnceUpToTheConfiguredDegreeAndNoFurther(int degree)
    {
        using var run = new GatedRun(6);
        var worker = run.Worker(degree);

        var result = worker.ExecuteAsync(Path.GetTempPath(), 6, Lease, TestBeat.Create(), default);
        await run.WaitForRunningAsync(degree);
        await Task.Delay(150);

        Assert.Equal(degree, run.Running);
        run.OpenGate();
        Assert.Equal(new EvidenceProcessingWorkerResult(6, 0), await result);
        Assert.Equal(degree, run.Peak);
    }

    [Fact]
    public async Task ShutdownCancelsEveryInFlightItemAndReleasesItsClaim()
    {
        using var run = new GatedRun(4);
        using var shutdown = new CancellationTokenSource();
        var worker = run.Worker(2);

        var result = worker.ExecuteAsync(Path.GetTempPath(), 4, Lease, TestBeat.Create(), shutdown.Token);
        await run.WaitForRunningAsync(2);
        await shutdown.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result);
        Assert.Equal(2, run.Released.Count);
        Assert.Equal(2, run.Released.Distinct().Count);
    }

    [Fact]
    public async Task EachFinishedItemBeatsSoAPassOfManySlowItemsDoesNotLookLikeAHungLoop()
    {
        using var run = new GatedRun(4);
        run.OpenGate();
        var heartbeats = new RecordingHeartbeatStore();

        await run.Worker(2).ExecuteAsync(Path.GetTempPath(), 4, Lease, TestBeat.Create(heartbeats, TimeSpan.Zero), default);

        Assert.Equal(4, heartbeats.Beats);
    }

    private sealed class GatedRun : IDisposable
    {
        private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource timeout = new(Timeout);
        private readonly List<EvidenceProcessingRecord> records;
        private int running;
        private int peak;

        public GatedRun(int count) =>
            records = [.. Enumerable.Range(0, count).Select(index => Record($"job-{index}"))];

        public int Running => Volatile.Read(ref running);
        public int Peak => Volatile.Read(ref peak);
        public ConcurrentReleases Released { get; } = new();

        public EvidenceProcessingWorker Worker(int degree)
        {
            var jobs = new FixedJobStore { Get = AwaitGateAsync };
            var preparation = new JobPreparation(jobs, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null, NullLogger<JobPreparation>.Instance);
            return new(jobs, preparation, new Store(records, Released), null!, null!, null!, degree,
                NullLogger<EvidenceProcessingWorker>.Instance);
        }

        public async Task WaitForRunningAsync(int count)
        {
            while (Running < count)
                await Task.Delay(10, timeout.Token);
        }

        public void OpenGate() => gate.SetResult();

        public void Dispose() => timeout.Dispose();

        private async Task<CollectionJobRecord?> AwaitGateAsync(string jobId, CancellationToken cancellationToken)
        {
            var now = Interlocked.Increment(ref running);
            var seen = Volatile.Read(ref peak);
            while (now > seen && Interlocked.CompareExchange(ref peak, now, seen) is var current && current != seen) seen = current;
            try { await gate.Task.WaitAsync(cancellationToken); }
            finally { Interlocked.Decrement(ref running); }
            return null;
        }

        private static EvidenceProcessingRecord Record(string jobId) => new(jobId, "attempt-" + jobId, "source",
            "https://example.test/page", EvidenceProcessingStage.Preparation, EvidenceProcessingStatus.Pending,
            null, 0, 0, null, null, null, null, null);
    }

    private sealed class ConcurrentReleases
    {
        private readonly Lock gate = new();
        private readonly List<string> items = [];

        public int Count { get { lock (gate) return items.Count; } }

        public IReadOnlyCollection<string> Distinct() { lock (gate) return [.. items.Distinct()]; }

        public void Add(string jobId) { lock (gate) items.Add(jobId); }
    }

    private sealed class Store(IReadOnlyList<EvidenceProcessingRecord> records, ConcurrentReleases released)
        : IEvidenceProcessingStore
    {
        public Task<IReadOnlyList<EvidenceProcessingRecord>> GetEligibleAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EvidenceProcessingRecord>>([.. records.Take(limit)]);

        public Task<EvidenceProcessingClaim?> TryClaimAsync(string jobId, string attemptId, TimeSpan leaseDuration,
            CancellationToken cancellationToken) => Task.FromResult<EvidenceProcessingClaim?>(
            new(records.Single(record => record.JobId == jobId), "lease-" + jobId, 1));

        public Task<bool> RenewAsync(EvidenceProcessingClaim claim, TimeSpan leaseDuration, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<bool> ReleaseAsync(EvidenceProcessingClaim claim, CancellationToken cancellationToken)
        {
            released.Add(claim.Record.JobId);
            return Task.FromResult(true);
        }

        public Task<bool> IsPredecessorSettledAsync(string attemptId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<EvidenceProcessingRecord>> GetByJobIdsAsync(IReadOnlyList<string> jobIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CollectionChangeEvent>> ListRecentChangesAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EvidenceProcessingRecord> EnsureAsync(string jobId, string attemptId, string sourceId, string requestedUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> WaitForPredecessorAsync(EvidenceProcessingClaim claim, string predecessorAttemptId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CheckpointAsync(EvidenceProcessingCheckpoint checkpoint, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
