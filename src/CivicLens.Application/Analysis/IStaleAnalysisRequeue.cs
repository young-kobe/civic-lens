namespace CivicLens.Application.Analysis;

public interface IStaleAnalysisRequeue
{
    Task<int> RequeueStaleAsync(CancellationToken cancellationToken);
}
