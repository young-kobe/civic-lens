using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;

namespace CivicLens.Tests.Application.Collection.Jobs;

public sealed class CollectionJobWorkerTests
{
    [Fact]
    public async Task EmptyQueueWaitsForDurableWakeupInsteadOfPolling()
    {
        var queue = new SequenceQueue((_, _) => Task.FromResult(new CollectionWorkerQueuePage([], null)));
        var runner = new RunCollectionJob(new RecordingJobStore(), null!, null!, null!, null!);
        var wakeup = new FakeWakeup(async token => await Task.Delay(Timeout.Infinite, token));
        var worker = new CollectionJobWorker(queue, runner, wakeup: wakeup);
        using var cancellation = new CancellationTokenSource();
        var cancelTask = Task.Run(async () =>
        {
            await Task.Delay(100);
            cancellation.Cancel();
        });

        var result = await worker.ExecuteAsync(Path.GetTempPath(), once: false,
            cancellationToken: cancellation.Token);
        await cancelTask;

        Assert.Equal(1, result.Passes);
        Assert.Single(queue.CursorHistory);
        Assert.Equal(0, result.JobsVisited);
        Assert.Equal(1, wakeup.ConnectCount);
        Assert.Equal(1, wakeup.WaitCount);
    }

    [Fact]
    public async Task ConnectsBeforeInitialScanSoAnArrivingNotificationCannotBeMissed()
    {
        using var cancellation = new CancellationTokenSource();
        var wakeup = new FakeWakeup(_ => { cancellation.Cancel(); return Task.CompletedTask; });
        var queue = new SequenceQueue((_, _) =>
        {
            Assert.True(wakeup.IsConnected);
            return Task.FromResult(new CollectionWorkerQueuePage([], null));
        });
        var worker = new CollectionJobWorker(queue,
            new RunCollectionJob(new RecordingJobStore(), null!, null!, null!, null!), wakeup: wakeup);

        _ = await worker.ExecuteAsync(Path.GetTempPath(), once: false, cancellationToken: cancellation.Token);

        Assert.Equal(1, wakeup.ConnectCount);
        Assert.Equal(1, wakeup.WaitCount);
    }

    [Fact]
    public async Task DueSourcesAreAdmittedBeforeQueueScanAndOnceDrainsTheAdmittedBatch()
    {
        var schedules = new RecordingScheduleStore(1, 0);
        var queue = new SequenceQueue((_, callNumber) =>
        {
            Assert.True(schedules.AdmissionCount >= callNumber);
            return Task.FromResult(callNumber == 1
                ? new CollectionWorkerQueuePage(["scheduled-job"], null)
                : new CollectionWorkerQueuePage([], null));
        });
        var worker = new CollectionJobWorker(queue,
            new RunCollectionJob(new RecordingJobStore(), null!, null!, null!, null!), schedules: schedules);

        var result = await worker.ExecuteAsync(Path.GetTempPath(), once: true, batchSize: 7);

        Assert.Equal(1, result.JobsVisited);
        Assert.Equal(2, schedules.AdmissionCount);
        Assert.Equal([7, 7], schedules.Limits);
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
            throw new InvalidOperationException("The cursor scan should end after the last page.");
        });
        var jobs = new RecordingJobStore();
        var wakeup = new FakeWakeup(token => { cancellation.Cancel(); return Task.CompletedTask; });
        var worker = new CollectionJobWorker(queue, new RunCollectionJob(jobs, null!, null!, null!, null!), wakeup: wakeup);

        var result = await worker.ExecuteAsync(Path.GetTempPath(), once: false,
            cancellationToken: cancellation.Token);

        Assert.Equal(["blocked-a", "blocked-b", "later-job"], jobs.ClaimedJobIds);
        Assert.Equal(3, result.JobsVisited);
        Assert.Equal(2, queue.CursorHistory.Count);
        Assert.Null(queue.CursorHistory[0]);
        Assert.Equal(cursor, queue.CursorHistory[1]);
        Assert.Equal(0, result.JobFailures);
    }

    [Fact]
    public async Task QueueAndPerJobFailuresDoNotStopLaterWorkOrSpin()
    {
        using var cancellation = new CancellationTokenSource();
        var queue = new SequenceQueue((_, callNumber) =>
        {
            if (callNumber == 1) throw new IOException("temporary queue failure");
            if (callNumber == 2)
                return Task.FromResult(new CollectionWorkerQueuePage(["fails-on-claim", "later-job"], null));
            return Task.FromResult(new CollectionWorkerQueuePage([], null));
        });
        var jobs = new RecordingJobStore("fails-on-claim");
        var wakeup = new FakeWakeup(token => { cancellation.Cancel(); return Task.CompletedTask; });
        var worker = new CollectionJobWorker(queue, new RunCollectionJob(jobs, null!, null!, null!, null!), wakeup: wakeup);

        var result = await worker.ExecuteAsync(Path.GetTempPath(), once: false,
            cancellationToken: cancellation.Token);

        Assert.Equal(["fails-on-claim", "later-job"], jobs.ClaimedJobIds);
        Assert.Equal(2, result.JobsVisited);
        Assert.Equal(1, result.QueueFailures);
        Assert.Equal(1, result.JobFailures);
        Assert.Equal(3, queue.CursorHistory.Count);
        Assert.Equal(1, wakeup.WaitCount);
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

    private sealed class FakeWakeup(Func<CancellationToken, Task> wait) : ICollectionPipelineWakeup
    {
        public int ConnectCount { get; private set; }
        public int WaitCount { get; private set; }
        public bool IsConnected { get; private set; }
        public Task ConnectAsync(CancellationToken cancellationToken) { ConnectCount++; IsConnected = true; return Task.CompletedTask; }
        public Task WaitAsync(CancellationToken cancellationToken) { WaitCount++; return wait(cancellationToken); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingScheduleStore(params int[] admissions) : ICollectionScheduleStore
    {
        public int AdmissionCount { get; private set; }
        public List<int> Limits { get; } = [];

        public Task SynchronizeAsync(CollectionConfiguration configuration, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<int> AdvanceDueAsync(int limit, CancellationToken cancellationToken)
        {
            Limits.Add(limit);
            var index = AdmissionCount++;
            return Task.FromResult(index < admissions.Length ? admissions[index] : 0);
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
