using CivicLens.Application.Analysis;

namespace CivicLens.Infrastructure.Analysis;

public abstract class PostgresAnalysisStore<TRun> : IAnalysisQueue<TRun> where TRun : class
{
    private readonly IAnalysisQueue<TRun> queue;

    internal PostgresAnalysisStore(IAnalysisQueue<TRun> queue) => this.queue = queue;

    public Task<IReadOnlyList<AnalysisRecord>> GetEligibleAsync(int limit, CancellationToken cancellationToken) =>
        queue.GetEligibleAsync(limit, cancellationToken);

    public Task<long> GetChargedTodayAsync(CancellationToken cancellationToken) => queue.GetChargedTodayAsync(cancellationToken);

    public Task<int> RequeueFailedAsync(string? subjectId, CancellationToken cancellationToken) =>
        queue.RequeueFailedAsync(subjectId, cancellationToken);

    public Task<AnalysisClaim?> TryClaimAsync(string subjectId, TimeSpan leaseDuration, CancellationToken cancellationToken) =>
        queue.TryClaimAsync(subjectId, leaseDuration, cancellationToken);

    public Task<bool> RenewAsync(AnalysisClaim claim, TimeSpan leaseDuration, CancellationToken cancellationToken) =>
        queue.RenewAsync(claim, leaseDuration, cancellationToken);

    public Task<AnalysisReservation> ReserveAsync(AnalysisClaim claim, string runId, string inputHash, long tokens,
        long dailyTokenLimit, CancellationToken cancellationToken) =>
        queue.ReserveAsync(claim, runId, inputHash, tokens, dailyTokenLimit, cancellationToken);

    public Task<bool> CheckpointAsync(AnalysisCheckpoint checkpoint, TRun? run, CancellationToken cancellationToken) =>
        queue.CheckpointAsync(checkpoint, run, cancellationToken);

    public Task<bool> ReleaseAsync(AnalysisClaim claim, CancellationToken cancellationToken) =>
        queue.ReleaseAsync(claim, cancellationToken);

    public Task<int> ReleaseBudgetWaitsAsync(CancellationToken cancellationToken) => queue.ReleaseBudgetWaitsAsync(cancellationToken);
}
