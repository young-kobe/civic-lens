using Microsoft.Extensions.Logging;

namespace CivicLens.Application;

/// <summary>Writes the heartbeat of one work loop. A failed write is logged, so the heartbeat goes stale.</summary>
public sealed class WorkerBeat(IWorkerHeartbeatStore store, string loop, ILogger<WorkerBeat> logger,
    TimeSpan? interval = null)
{
    private readonly TimeSpan interval = interval ?? TimeSpan.FromMinutes(1);

    public async Task BeatAsync(CancellationToken cancellationToken)
    {
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

    /// <summary>Beats once, then beats at each interval until the wait ends. The wait itself is never cancelled.</summary>
    public async Task WaitAsync(Func<CancellationToken, Task> wait, CancellationToken cancellationToken)
    {
        await BeatAsync(cancellationToken);
        var waiting = wait(cancellationToken);
        using var finished = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   await Task.WhenAny(waiting, Task.Delay(interval, finished.Token)) != waiting)
                await BeatAsync(cancellationToken);
        }
        finally
        {
            await finished.CancelAsync();
        }
        await waiting;
    }
}
