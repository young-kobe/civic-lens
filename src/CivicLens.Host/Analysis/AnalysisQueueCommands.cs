using CivicLens.Application.Analysis;

namespace CivicLens.Host.Analysis;

internal sealed class AnalysisQueueCommands<TRun, TView, TItem>(IAnalysisQueueStore<TRun, TView, TItem> store, IAnalysisStatusStore status)
    where TRun : class
    where TView : struct, Enum
{
    public GetAnalysisStatus<TRun, TView, TItem> Status { get; } = new(store, status);
    public ListAnalyses<TRun, TView, TItem> List { get; } = new(store);
    public GetAnalysisRun<TRun, TView, TItem> Run { get; } = new(store);
    public RequeueAnalyses<TRun> Requeue { get; } = new(store);
}
