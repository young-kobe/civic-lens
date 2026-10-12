using CivicLens.Application.Analysis;

namespace CivicLens.Host.Collection;

internal sealed record AnalysisStage(string Name, Func<bool, CancellationToken, Task<AnalysisWorkerResult>> RunAsync)
{
    public static AnalysisStage? For<TRun>(string name, AnalysisWorker<TRun>? worker) where TRun : class =>
        worker is null ? null : new(name, (once, token) => worker.ExecuteAsync(once, token));
}
