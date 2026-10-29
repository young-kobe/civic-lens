using CivicLens.Application.Analysis;
using Microsoft.Extensions.Logging;

namespace CivicLens.Application;

/// <summary>Runs one pass, then sleeps until the wakeup. A pass returns its progress count. A failed pass is logged and counted.</summary>
public sealed class StageWorker(string name, Func<CancellationToken, Task<int>> pass, IWorkerWakeup wakeup, ILogger<StageWorker> logger,
    WorkerBeat beat)
{
    public async Task<AnalysisWorkerResult> ExecuteAsync(bool once, CancellationToken cancellationToken = default)
    {
        await beat.BeatAsync(cancellationToken);
        var failures = once ? 0 : await WorkerLoop.ConnectUntilAvailableAsync(wakeup, cancellationToken);
        var passes = 0;
        var progress = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                passes++;
                await beat.BeatAsync(cancellationToken);
                try
                {
                    progress += await pass(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception exception) when (WorkerLoop.IsRecoverable(exception))
                {
                    failures++;
                    logger.LogError(exception, "Stage {Stage} failed a pass.", name);
                }
                if (once) break;
                failures += await WorkerLoop.WaitForWorkAsync(wakeup, false, beat, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        return new(passes, progress, failures);
    }
}
