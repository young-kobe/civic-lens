using CivicLens.Application.Analysis;

namespace CivicLens.Application;

/// <summary>Runs one pass, then sleeps until the wakeup. A pass returns its progress count. A failed pass is reported and counted.</summary>
public sealed class StageWorker(Func<CancellationToken, Task<int>> pass, IWorkerWakeup wakeup, Action<Exception> reportFailure)
{
    public async Task<AnalysisWorkerResult> ExecuteAsync(bool once, CancellationToken cancellationToken = default)
    {
        var failures = once ? 0 : await WorkerLoop.ConnectUntilAvailableAsync(wakeup, cancellationToken);
        var passes = 0;
        var progress = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                passes++;
                try
                {
                    progress += await pass(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception exception) when (WorkerLoop.IsRecoverable(exception))
                {
                    failures++;
                    reportFailure(exception);
                }
                if (once) break;
                failures += await WorkerLoop.WaitForWorkAsync(wakeup, false, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        return new(passes, progress, failures);
    }
}
