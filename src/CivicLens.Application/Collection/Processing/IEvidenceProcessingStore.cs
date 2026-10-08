namespace CivicLens.Application.Collection.Processing;

public interface IEvidenceProcessingStore
{
    Task<EvidenceProcessingPage> GetPreparationJobIdsAsync(EvidenceProcessingCursor? cursor, int limit,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<EvidenceProcessingRecord>> GetEligibleAsync(int limit, CancellationToken cancellationToken);
    Task<EvidenceProcessingRecord?> GetByAttemptIdAsync(string attemptId, CancellationToken cancellationToken);
    Task<bool> IsAwaitingPreparationAsync(string attemptId, CancellationToken cancellationToken);
    Task<IReadOnlyList<EvidenceProcessingRecord>> GetByJobIdsAsync(IReadOnlyList<string> jobIds,
        CancellationToken cancellationToken);
    Task<EvidenceProcessingRecord> EnsureAsync(string jobId, string attemptId, string sourceId, string requestedUrl,
        CancellationToken cancellationToken);
    Task EnsurePreparationAsync(string jobId, string sourceId, string requestedUrl, CancellationToken cancellationToken);
    Task<EvidenceProcessingClaim?> TryClaimAsync(string jobId, string attemptId, TimeSpan leaseDuration,
        CancellationToken cancellationToken);
    Task<bool> RenewAsync(EvidenceProcessingClaim claim, TimeSpan leaseDuration, CancellationToken cancellationToken);
    Task<bool> DeferAsync(EvidenceProcessingClaim claim, DateTimeOffset retryAt, CancellationToken cancellationToken);
    Task<bool> CheckpointAsync(EvidenceProcessingCheckpoint checkpoint, CancellationToken cancellationToken);
    Task<bool> ReleaseAsync(EvidenceProcessingClaim claim, CancellationToken cancellationToken);
}
