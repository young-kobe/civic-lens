using CivicLens.Application;
using CivicLens.Tests.Fixtures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.Application;

public sealed class WorkerBeatTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(20);

    [Fact]
    public async Task AnIdleWaitKeepsBeatingSoAQuietButHealthyLoopStaysFresh()
    {
        var store = new RecordingHeartbeatStore();
        var beat = new WorkerBeat(store, "loop", NullLogger<WorkerBeat>.Instance, Interval);
        var wake = new TaskCompletionSource();

        var waiting = beat.WaitAsync(_ => wake.Task, default);
        await Eventually(() => store.Beats >= 3);
        wake.SetResult();
        await waiting;

        Assert.True(store.Beats >= 3);
    }

    [Fact]
    public async Task ALoopThatIsStuckInsideAPassWritesNoBeatSoItsHeartbeatGoesStale()
    {
        var store = new RecordingHeartbeatStore();
        var beat = new WorkerBeat(store, "loop", NullLogger<WorkerBeat>.Instance, Interval);
        using var stop = new CancellationTokenSource();
        var worker = new StageWorker("test", token => Task.Delay(Timeout.Infinite, token).ContinueWith(_ => 0, TaskScheduler.Default),
            new CountingWakeup(), NullLogger<StageWorker>.Instance, beat);

        var running = worker.ExecuteAsync(once: false, stop.Token);
        await Task.Delay(Interval * 8);
        var beatsWhileStuck = store.Beats;
        await stop.CancelAsync();
        await running;

        Assert.Equal(0, beatsWhileStuck);
    }

    [Fact]
    public async Task AFinishedPassBeatsBeforeTheLoopSleeps()
    {
        var store = new RecordingHeartbeatStore();
        var beat = new WorkerBeat(store, "loop", NullLogger<WorkerBeat>.Instance, TimeSpan.FromMinutes(1));
        using var stop = new CancellationTokenSource();
        var wakeup = new CountingWakeup(() => stop.Cancel());

        await new StageWorker("test", _ => Task.FromResult(1), wakeup, NullLogger<StageWorker>.Instance, beat).ExecuteAsync(false, stop.Token);

        Assert.Equal(1, store.Beats);
    }

    [Fact]
    public async Task AFailedBeatIsLoggedAndTheLoopKeepsWaiting()
    {
        var logger = new CapturingLogger<WorkerBeat>();
        var beat = new WorkerBeat(new RecordingHeartbeatStore(failing: true), "loop", logger, Interval);

        await beat.WaitAsync(_ => Task.Delay(Interval * 4), default);

        var entry = logger.Entries.First();
        Assert.Equal((LogLevel.Error, "down"), (entry.Level, entry.Exception!.Message));
    }

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(5);
    }
}
