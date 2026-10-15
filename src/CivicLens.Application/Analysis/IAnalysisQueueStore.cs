namespace CivicLens.Application.Analysis;

public interface IAnalysisQueueStore<TRun, TView, TItem> : IAnalysisQueue<TRun>
    where TRun : class
    where TView : struct, Enum
{
    Task<TRun?> GetRunAsync(string runId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TItem>> ListAsync(TView? view, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<TView, int>> CountAsync(CancellationToken cancellationToken);
}
