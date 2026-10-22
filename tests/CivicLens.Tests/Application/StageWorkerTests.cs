using CivicLens.Application;
using CivicLens.Tests.Fixtures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.Application;

public sealed class StageWorkerTests
{
    [Fact]
    public async Task AFailedPassIsReportedAndCountedAndTheProgressOfOtherPassesStaysSoTheOwnerSeesEveryFailure()
    {
        var calls = 0;
        var logger = new CapturingLogger<StageWorker>();
        using var stop = new CancellationTokenSource();
        var wakeup = new CountingWakeup(() => { if (calls == 2) stop.Cancel(); });
        var worker = new StageWorker("test", _ => ++calls == 1 ? Task.FromException<int>(new InvalidOperationException("down")) : Task.FromResult(3),
            wakeup, logger);

        var result = await worker.ExecuteAsync(once: false, stop.Token);

        Assert.Equal((2, 3, 1), (result.Passes, result.ProgressCount, result.Failures));
        var entry = Assert.Single(logger.Entries);
        Assert.Equal((LogLevel.Error, "down"), (entry.Level, entry.Exception!.Message));
    }

    [Fact]
    public async Task ARunThatIsNotOnceSleepsUntilTheWakeupAndThenPassesAgain()
    {
        var passes = 0;
        using var stop = new CancellationTokenSource();
        var wakeup = new CountingWakeup(() => { if (passes == 2) stop.Cancel(); });

        var result = await new StageWorker("test", _ => Task.FromResult(++passes), wakeup, NullLogger<StageWorker>.Instance).ExecuteAsync(once: false, stop.Token);

        Assert.Equal((2, 3, 2), (result.Passes, result.ProgressCount, wakeup.WaitCount));
    }

    [Fact]
    public async Task AOnceRunPassesExactlyOnceAndNeverWaits()
    {
        var wakeup = new CountingWakeup();

        var result = await new StageWorker("test", _ => Task.FromResult(5), wakeup, NullLogger<StageWorker>.Instance).ExecuteAsync(once: true);

        Assert.Equal((1, 5, 0), (result.Passes, result.ProgressCount, wakeup.WaitCount));
    }
}
