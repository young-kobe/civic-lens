using Microsoft.Extensions.Logging;

namespace CivicLens.Application;

/// <summary>Writes the heartbeat of one work loop. A failed write is logged, so the heartbeat goes stale.</summary>
public sealed class WorkerBeat(IWorkerHeartbeatStore store, string loop, ILogger<WorkerBeat> logger, TimeProvider clock,
    TimeSpan? interval = null)
{
    private readonly TimeSpan interval = interval ?? TimeSpan.FromMinutes(1);
    private long lastBeatTicks = long.MinValue;

    public async Task BeatAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref lastBeatTicks, clock.GetUtcNow().Ticks);
        try
        {
            await store.BeatAsync(loop, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (WorkerLoop.IsRecoverable(exception))
        {
            logger.LogError(exception, "Loop {Loop} could not write its heartbeat.", loop);
        }
    }

    /// <summary>Skips the write when this loop beat less than one interval ago, so fast items do not write per item.</summary>
    public Task BeatIfDueAsync(CancellationToken cancellationToken)
    {
        var last = Volatile.Read(ref lastBeatTicks);
        return last != long.MinValue && clock.GetUtcNow().Ticks - last < interval.Ticks
            ? Task.CompletedTask
            : BeatAsync(cancellationToken);
    }

    /// <summary>Beats once, then beats at each interval until the wait ends. The wait itself is never cancelled.</summary>
    public async Task WaitAsync(Func<CancellationToken, Task> wait, CancellationToken cancellationToken)
    {
        await BeatAsync(cancellationToken);
        var waiting = wait(cancellationToken);
        using var finished = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   await Task.WhenAny(waiting, Task.Delay(interval, clock, finished.Token)) != waiting)
                await BeatAsync(cancellationToken);
        }
        finally
        {
            await finished.CancelAsync();
        }
        await waiting;
    }
}
