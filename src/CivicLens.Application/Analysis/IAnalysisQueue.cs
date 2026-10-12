namespace CivicLens.Application.Analysis;

public interface IAnalysisQueue<TRun> where TRun : class
{
    Task<IReadOnlyList<AnalysisRecord>> GetEligibleAsync(int limit, CancellationToken cancellationToken);
    Task<long> GetChargedTodayAsync(CancellationToken cancellationToken);
    Task<int> RequeueFailedAsync(string? subjectId, CancellationToken cancellationToken);
    Task<AnalysisClaim?> TryClaimAsync(string subjectId, TimeSpan leaseDuration, CancellationToken cancellationToken);
    Task<bool> RenewAsync(AnalysisClaim claim, TimeSpan leaseDuration, CancellationToken cancellationToken);
    Task<AnalysisReservation> ReserveAsync(AnalysisClaim claim, string runId, string inputHash, long tokens, long dailyTokenLimit,
        CancellationToken cancellationToken);
    Task<bool> CheckpointAsync(AnalysisCheckpoint checkpoint, TRun? run, CancellationToken cancellationToken);
    Task<bool> ReleaseAsync(AnalysisClaim claim, CancellationToken cancellationToken);
    Task<int> ReleaseBudgetWaitsAsync(CancellationToken cancellationToken);
}
