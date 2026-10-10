using CivicLens.Core.Analysis;

namespace CivicLens.Application.Analysis;

public enum DocumentChangeAnalysisReservation { Reserved, WaitingForBudget, LeaseLost }

public interface IDocumentChangeAnalysisStore
{
    Task<IReadOnlyList<DocumentChangeAnalysisRecord>> GetEligibleAsync(int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<DocumentChangeAnalysisRecord>> GetByComparisonIdsAsync(IReadOnlyCollection<string> comparisonIds,
        CancellationToken cancellationToken);
    Task<AnalysisRun?> GetDraftRunAsync(string draftId, CancellationToken cancellationToken);
    Task<AnalysisRun?> GetRunAsync(string runId, CancellationToken cancellationToken);
    Task<long> GetChargedTodayAsync(CancellationToken cancellationToken);
    Task<int> RequeueFailedAsync(string? comparisonId, CancellationToken cancellationToken);
    Task<DocumentChangeAnalysisEvidence?> ReadEvidenceAsync(string comparisonId, CancellationToken cancellationToken);
    Task<DocumentChangeAnalysisClaim?> TryClaimAsync(string comparisonId, TimeSpan leaseDuration, CancellationToken cancellationToken);
    Task<bool> RenewAsync(DocumentChangeAnalysisClaim claim, TimeSpan leaseDuration, CancellationToken cancellationToken);
    Task<DocumentChangeAnalysisReservation> ReserveAsync(DocumentChangeAnalysisClaim claim, string runId, string inputHash,
        long tokens, long dailyTokenLimit, CancellationToken cancellationToken);

    Task<bool> CheckpointAsync(DocumentChangeAnalysisCheckpoint checkpoint, AnalysisRun? run, CancellationToken cancellationToken);
    Task<bool> ReleaseAsync(DocumentChangeAnalysisClaim claim, CancellationToken cancellationToken);
    Task<int> ReleaseBudgetWaitsAsync(CancellationToken cancellationToken);
}
