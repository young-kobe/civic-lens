using Microsoft.Extensions.Logging.Abstractions;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.Jobs;

public sealed partial class CollectionJobWorkerTests
{
    private static readonly TimeSpan LaneTimeout = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task JobsOfDifferentPacingKeysRunAtOnceUpToTheConfiguredLanesAndNoFurther(int lanes)
    {
        using var run = new LaneRun(("a", "1"), ("b", "1"), ("c", "1"), ("d", "1"), ("e", "1"), ("f", "1"));
        var result = run.StartAsync(lanes);
        await run.WaitForRunningAsync(lanes);
        await Task.Delay(150);

        Assert.Equal(lanes, run.Running);
        run.OpenGate();
        Assert.Equal(6, (await result).JobsVisited);
        Assert.Equal(lanes, run.Peak);
    }

    [Fact]
    public async Task JobsOfOnePacingKeyNeverOverlapAndKeepQueueOrder()
    {
        using var run = new LaneRun(("a", "1"), ("b", "1"), ("a", "2"), ("b", "2"), ("a", "3"), ("b", "3"));
        var result = run.StartAsync(4);
        await run.WaitForRunningAsync(2);
        await Task.Delay(150);
        Assert.Equal(2, run.Running);

        run.OpenGate();
        Assert.Equal(6, (await result).JobsVisited);
        Assert.Equal(1, run.PeakOfKey("a"));
        Assert.Equal(1, run.PeakOfKey("b"));
        Assert.Equal(["a1", "a2", "a3"], run.StartedOfKey("a"));
        Assert.Equal(["b1", "b2", "b3"], run.StartedOfKey("b"));
    }

    [Fact]
    public async Task OneLaneKeepsTheExactQueueOrderAcrossKeys()
    {
        using var run = new LaneRun(("a", "1"), ("b", "1"), ("a", "2"), ("c", "1"), ("b", "2"));
        run.OpenGate();

        await run.StartAsync(null);

        Assert.Equal(["a1", "b1", "a2", "c1", "b2"], run.Started);
        Assert.Equal(1, run.Peak);
    }

    [Fact]
    public async Task CountsStayCorrectWhenLanesFailAtTheSameTime()
    {
        using var run = new LaneRun(("a", "1"), ("b", "1"), ("c", "1"), ("a", "2"), ("b", "2"), ("c", "2"))
        {
            FailingJobs = ["b1", "c2"]
        };
        run.OpenGate();

        var result = await run.StartAsync(3);

        Assert.Equal(6, result.JobsVisited);
        Assert.Equal(2, result.JobFailures);
        Assert.Equal(1, result.Passes);
    }

    [Fact]
    public async Task ShutdownStopsEveryLaneAndStartsNoLaterJob()
    {
        using var run = new LaneRun(("a", "1"), ("b", "1"), ("a", "2"), ("b", "2"));
        using var shutdown = new CancellationTokenSource();
        var result = run.StartAsync(2, shutdown.Token);
        await run.WaitForRunningAsync(2);

        await shutdown.CancelAsync();

        Assert.Equal(2, (await result).JobsVisited);
        Assert.Equal(0, run.Running);
        Assert.Equal(["a1", "b1"], run.Started.Order());
    }

    [Fact]
    public void ALaneCountOutsideTheBoundsIsRefused()
    {
        var queue = new SequenceQueue((_, _) => Task.FromResult(Page(null)));
        var runner = new RunCollectionJob(new RecordingJobStore(), null!, null!, null!, null!, NullLogger<RunCollectionJob>.Instance);

        Assert.Throws<ArgumentOutOfRangeException>(() => new CollectionJobWorker(queue, runner,
            NullLogger<CollectionJobWorker>.Instance, TestBeat.Create(), concurrency: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollectionJobWorker(queue, runner,
            NullLogger<CollectionJobWorker>.Instance, TestBeat.Create(), concurrency: CollectionLanePolicy.MaximumConcurrency + 1));
    }

    private sealed class LaneRun : IDisposable
    {
        private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource timeout = new(LaneTimeout);
        private readonly Lock sync = new();
        private readonly (string Key, string Id)[] jobs;
        private readonly List<string> started = [];
        private readonly Dictionary<string, int> runningOfKey = [];
        private readonly Dictionary<string, int> peakOfKey = [];
        private int running;
        private int peak;

        public LaneRun(params (string Key, string Number)[] jobs) =>
            this.jobs = [.. jobs.Select(job => (job.Key, job.Key + job.Number))];

        public string[] FailingJobs { get; init; } = [];
        public int Running { get { lock (sync) return running; } }
        public int Peak { get { lock (sync) return peak; } }
        public List<string> Started { get { lock (sync) return [.. started]; } }
        public int PeakOfKey(string key) { lock (sync) return peakOfKey[key]; }
        public string[] StartedOfKey(string key) { lock (sync) return [.. started.Where(id => id.StartsWith(key, StringComparison.Ordinal))]; }

        public Task<CollectionJobWorkerResult> StartAsync(int? lanes, CancellationToken cancellationToken = default)
        {
            var store = new RecordingJobStore(FailingJobs) { OnClaim = HoldAsync };
            var queue = new SequenceQueue((_, call) => Task.FromResult(call == 1
                ? new CollectionWorkerQueuePage([.. jobs.Select(job => new CollectionWorkerQueueJob(job.Id, job.Key))], null)
                : new CollectionWorkerQueuePage([], null)));
            var runner = new RunCollectionJob(store, null!, null!, null!, null!, NullLogger<RunCollectionJob>.Instance);
            var worker = lanes is null
                ? new CollectionJobWorker(queue, runner, NullLogger<CollectionJobWorker>.Instance, TestBeat.Create())
                : new CollectionJobWorker(queue, runner, NullLogger<CollectionJobWorker>.Instance, TestBeat.Create(),
                    concurrency: lanes.Value);
            return worker.ExecuteAsync(Path.GetTempPath(), once: true, cancellationToken: cancellationToken);
        }

        public async Task WaitForRunningAsync(int count)
        {
            while (Running < count)
                await Task.Delay(10, timeout.Token);
        }

        public void OpenGate() => gate.SetResult();

        public void Dispose() => timeout.Dispose();

        private async Task HoldAsync(string jobId, CancellationToken cancellationToken)
        {
            var key = jobs.Single(job => job.Id == jobId).Key;
            lock (sync)
            {
                started.Add(jobId);
                running++;
                peak = Math.Max(peak, running);
                runningOfKey[key] = runningOfKey.GetValueOrDefault(key) + 1;
                peakOfKey[key] = Math.Max(peakOfKey.GetValueOrDefault(key), runningOfKey[key]);
            }
            try { await gate.Task.WaitAsync(timeout.Token).WaitAsync(cancellationToken); }
            finally
            {
                lock (sync)
                {
                    running--;
                    runningOfKey[key]--;
                }
            }
        }
    }
}
