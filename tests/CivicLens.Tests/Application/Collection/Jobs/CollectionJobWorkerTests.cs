using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;

namespace CivicLens.Tests.Application.Collection.Jobs;

public sealed class CollectionJobWorkerTests
{
    [Fact]
    public async Task EmptyQueueAwaitsCancellablePollDelayInsteadOfSpinning()
    {
        var queue = new SequenceQueue((_, _) => Task.FromResult(new CollectionWorkerQueuePage([], null)));
        var runner = new RunCollectionJob(new RecordingJobStore(), null!, null!, null!, null!);
        var worker = new CollectionJobWorker(queue, runner);
        using var cancellation = new CancellationTokenSource();
        var cancelTask = Task.Run(async () =>
        {
            await Task.Delay(100);
            cancellation.Cancel();
        });

        var result = await worker.ExecuteAsync(Path.GetTempPath(), once: false,
            pollInterval: TimeSpan.FromMilliseconds(250), cancellationToken: cancellation.Token);
        await cancelTask;

        Assert.Equal(1, result.Passes);
        Assert.Single(queue.CursorHistory);
        Assert.Equal(0, result.JobsVisited);
    }

    [Fact]
    public async Task CursorPassesClaimedPageAndWrapsAfterLaterJobsAreVisited()
    {
        var cursor = new CollectionWorkerCursor(10, "job-b");
        using var cancellation = new CancellationTokenSource();
        var queue = new SequenceQueue((receivedCursor, callNumber) =>
        {
            if (callNumber == 1)
            {
                Assert.Null(receivedCursor);
                return Task.FromResult(new CollectionWorkerQueuePage(["blocked-a", "blocked-b"], cursor));
            }
            if (callNumber == 2)
            {
                Assert.Equal(cursor, receivedCursor);
                return Task.FromResult(new CollectionWorkerQueuePage(["later-job"], null));
            }
            Assert.Null(receivedCursor);
            cancellation.Cancel();
            return Task.FromResult(new CollectionWorkerQueuePage([], null));
        });
        var jobs = new RecordingJobStore();
        var worker = new CollectionJobWorker(queue, new RunCollectionJob(jobs, null!, null!, null!, null!));

        var result = await worker.ExecuteAsync(Path.GetTempPath(), once: false,
            pollInterval: TimeSpan.FromMilliseconds(250), cancellationToken: cancellation.Token);

        Assert.Equal(["blocked-a", "blocked-b", "later-job"], jobs.ClaimedJobIds);
        Assert.Equal(3, result.JobsVisited);
        Assert.Equal(3, queue.CursorHistory.Count);
        Assert.Null(queue.CursorHistory[0]);
        Assert.Equal(cursor, queue.CursorHistory[1]);
        Assert.Null(queue.CursorHistory[2]);
        Assert.Equal(0, result.JobFailures);
    }

    [Fact]
    public async Task QueueAndPerJobFailuresDoNotStopLaterWorkOrSpin()
    {
        using var cancellation = new CancellationTokenSource();
        var queueTimes = new List<DateTimeOffset>();
        var queue = new SequenceQueue((_, callNumber) =>
        {
            queueTimes.Add(DateTimeOffset.UtcNow);
            if (callNumber == 1) throw new IOException("temporary queue failure");
            if (callNumber == 2)
                return Task.FromResult(new CollectionWorkerQueuePage(["fails-on-claim", "later-job"], null));
            cancellation.Cancel();
            return Task.FromResult(new CollectionWorkerQueuePage([], null));
        });
        var jobs = new RecordingJobStore("fails-on-claim");
        var worker = new CollectionJobWorker(queue, new RunCollectionJob(jobs, null!, null!, null!, null!));

        var result = await worker.ExecuteAsync(Path.GetTempPath(), once: false,
            pollInterval: TimeSpan.FromMilliseconds(250), cancellationToken: cancellation.Token);

        Assert.Equal(["fails-on-claim", "later-job"], jobs.ClaimedJobIds);
        Assert.Equal(2, result.JobsVisited);
        Assert.Equal(1, result.QueueFailures);
        Assert.Equal(1, result.JobFailures);
        Assert.Equal(3, queue.CursorHistory.Count);
        Assert.True(queueTimes[1] - queueTimes[0] >= TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public void DefaultLeaseMatchesExistingManualJobRunner()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), CollectionJobWorker.DefaultLeaseDuration);
    }

    private sealed class SequenceQueue(Func<CollectionWorkerCursor?, int, Task<CollectionWorkerQueuePage>> next)
        : ICollectionWorkerQueue
    {
        public List<CollectionWorkerCursor?> CursorHistory { get; } = [];

        public Task<CollectionWorkerQueuePage> GetEligibleJobIdsAsync(CollectionWorkerCursor? cursor, int limit,
            CancellationToken cancellationToken)
        {
            CursorHistory.Add(cursor);
            return next(cursor, CursorHistory.Count);
        }
    }

    private sealed class RecordingJobStore(params string[] failingJobIds) : ICollectionJobStore
    {
        public List<string> ClaimedJobIds { get; } = [];

        public Task<CollectionJobRecord> EnqueueAsync(ConfiguredCollectionSource source, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobRecord> EnqueueAsync(CollectionJobDefinition definition, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobRecord?> GetAsync(string jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CollectionJobRecord>> ListAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobRecord?> CancelAsync(string jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobClaim?> TryClaimAsync(string jobId, TimeSpan leaseDuration, CancellationToken cancellationToken)
        {
            ClaimedJobIds.Add(jobId);
            if (failingJobIds.Contains(jobId, StringComparer.Ordinal)) throw new IOException("temporary claim failure");
            return Task.FromResult<CollectionJobClaim?>(null);
        }
        public Task<CollectionJobRenewal> RenewAsync(CollectionJobLease jobLease, CollectionCollectorLease? collectorLease, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobStartResult> TryStartAttemptAsync(CollectionJobLease jobLease, string artifactDirectory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ReleaseCollectorAsync(CollectionJobLease jobLease, CollectionCollectorLease collectorLease, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SettleAttemptAsync(CollectionJobLease jobLease, string attemptId, CollectionAttemptResolution resolution, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ReleaseClaimAsync(CollectionJobLease jobLease, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
