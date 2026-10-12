using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public interface IStatementAnalysisStore
{
    Task<IReadOnlyList<StatementAnalysisRecord>> GetEligibleAsync(int limit, CancellationToken cancellationToken);
    Task<StatementGateRun?> GetRunAsync(string runId, CancellationToken cancellationToken);
    Task<long> GetChargedTodayAsync(CancellationToken cancellationToken);
    Task<StatementGateEvidence?> ReadEvidenceAsync(string candidateId, CancellationToken cancellationToken);
    Task<StatementAnalysisClaim?> TryClaimAsync(string candidateId, TimeSpan leaseDuration, CancellationToken cancellationToken);
    Task<bool> RenewAsync(StatementAnalysisClaim claim, TimeSpan leaseDuration, CancellationToken cancellationToken);
    Task<DocumentChangeAnalysisReservation> ReserveAsync(StatementAnalysisClaim claim, string runId, string inputHash,
        long tokens, long dailyTokenLimit, CancellationToken cancellationToken);
    Task<bool> CheckpointAsync(StatementAnalysisCheckpoint checkpoint, StatementGateRun? run, CancellationToken cancellationToken);
    Task<bool> ReleaseAsync(StatementAnalysisClaim claim, CancellationToken cancellationToken);
    Task<int> ReleaseBudgetWaitsAsync(CancellationToken cancellationToken);
    Task<int> RequeueFailedAsync(string? candidateId, CancellationToken cancellationToken);
    Task<int> RegateAsync(string issueRevisionId, DateOnly? since, CancellationToken cancellationToken);
    Task<IReadOnlyList<StatementGateListItem>> ListAsync(StatementGateView? view, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<StatementGateView, int>> CountAsync(CancellationToken cancellationToken);
}
