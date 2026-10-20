using CivicLens.Application.Analysis;

namespace CivicLens.Application.Publication;

public sealed class FactPublishingWorker(PublishCodeApprovedFacts publish, IWorkerWakeup wakeup, Action<Exception> reportFailure)
{
    public async Task<AnalysisWorkerResult> ExecuteAsync(bool once, CancellationToken cancellationToken = default)
    {
        var failures = once ? 0 : await WorkerLoop.ConnectUntilAvailableAsync(wakeup, cancellationToken);
        var passes = 0;
        var published = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                passes++;
                try
                {
                    published += await publish.ExecuteAsync(cancellationToken);
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
        return new(passes, published, failures);
    }
}
