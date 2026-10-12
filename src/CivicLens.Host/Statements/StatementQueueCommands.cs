using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;

namespace CivicLens.Host.Statements;

internal sealed class StatementQueueCommands<TRun, TView, TItem>(IStatementQueueStore<TRun, TView, TItem> store, IAnalysisStatusStore status)
    where TRun : class
    where TView : struct, Enum
{
    public GetStatementAnalysisStatus<TRun, TView, TItem> Status { get; } = new(store, status);
    public ListStatementAnalyses<TRun, TView, TItem> List { get; } = new(store);
    public GetStatementAnalysisRun<TRun, TView, TItem> Run { get; } = new(store);
    public RequeueAnalyses<TRun> Requeue { get; } = new(store);
}
