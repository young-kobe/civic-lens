using CivicLens.Core.Analysis;

namespace CivicLens.Application.Analysis;

public interface IAnalysisTaskStep<TRun> where TRun : class
{
    IAnalysisQueue<TRun> Queue { get; }
    AnalysisSettings Settings { get; }
    AnalysisTaskIdentity Identity { get; }
    TRun Wrap(AnalysisRun run);
    Task<bool> RunAsync(IAnalysisAttempt<TRun> attempt, CancellationToken cancellationToken);
}
