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
        var wake = new TaskCompletionSource();

        var waiting = TestBeat.Create(store, Interval).WaitAsync(_ => wake.Task, default);
        await Eventually(() => store.Beats >= 3);
        wake.SetResult();
        await waiting;

        Assert.True(store.Beats >= 3);
    }

    [Fact]
    public async Task ALoopStuckInsideAPassStopsBeatingAfterItsLastWriteSoItsHeartbeatGoesStale()
    {
        var store = new RecordingHeartbeatStore();
        using var stop = new CancellationTokenSource();
        var worker = new StageWorker("test", token => Task.Delay(Timeout.Infinite, token).ContinueWith(_ => 0, TaskScheduler.Default),
            new CountingWakeup(), NullLogger<StageWorker>.Instance, TestBeat.Create(store, Interval));

        var running = worker.ExecuteAsync(once: false, stop.Token);
        await Task.Delay(Interval * 8);
        var beatsWhileStuck = store.Beats;
        await stop.CancelAsync();
        await running;

        Assert.Equal(2, beatsWhileStuck);
    }

    [Fact]
    public async Task AStageBeatsAtStartAndBeforeEachPassSoALoopHungBeforeItsFirstPassIsStillSeen()
    {
        var store = new RecordingHeartbeatStore();
        using var stop = new CancellationTokenSource();
        var wakeup = new CountingWakeup(() => stop.Cancel());

        await new StageWorker("test", _ => Task.FromResult(1), wakeup, NullLogger<StageWorker>.Instance,
            TestBeat.Create(store, TimeSpan.FromMinutes(1))).ExecuteAsync(false, stop.Token);

        Assert.Equal(3, store.Beats);
    }

    [Fact]
    public async Task AnItemBoundaryBeatsOnlyWhenOneIntervalHasPassedSoFastItemsDoNotWritePerItem()
    {
        var store = new RecordingHeartbeatStore();
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var beat = TestBeat.Create(store, TimeSpan.FromMinutes(1), clock);

        await beat.BeatIfDueAsync(default);
        await beat.BeatIfDueAsync(default);
        clock.Now += TimeSpan.FromSeconds(59);
        await beat.BeatIfDueAsync(default);
        clock.Now += TimeSpan.FromSeconds(2);
        await beat.BeatIfDueAsync(default);

        Assert.Equal(2, store.Beats);
    }

    [Fact]
    public async Task AFailedBeatIsLoggedAndTheLoopKeepsBeatingAfterIt()
    {
        var logger = new CapturingLogger<WorkerBeat>();
        var store = new RecordingHeartbeatStore(failing: true);

        await TestBeat.Create(store, Interval, logger: logger).WaitAsync(_ => Task.Delay(Interval * 6), default);

        var entry = logger.Entries.First();
        Assert.Equal((LogLevel.Error, "down"), (entry.Level, entry.Exception!.Message));
        Assert.True(store.Beats >= 3);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(5);
    }
}
