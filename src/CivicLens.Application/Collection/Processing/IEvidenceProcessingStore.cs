namespace CivicLens.Application.Collection.Processing;

public interface IEvidenceProcessingStore
{
    Task<IReadOnlyList<EvidenceProcessingRecord>> GetEligibleAsync(int limit, CancellationToken cancellationToken);
    Task<EvidenceProcessingRecord?> GetByAttemptIdAsync(string attemptId, CancellationToken cancellationToken);
    Task<bool> IsAwaitingPreparationAsync(string attemptId, CancellationToken cancellationToken);
    Task<IReadOnlyList<EvidenceProcessingRecord>> GetByJobIdsAsync(IReadOnlyList<string> jobIds,
        CancellationToken cancellationToken);
    Task<EvidenceProcessingRecord> EnsureAsync(string jobId, string attemptId, string sourceId, string requestedUrl,
        CancellationToken cancellationToken);
    Task<EvidenceProcessingClaim?> TryClaimAsync(string jobId, string attemptId, TimeSpan leaseDuration,
        CancellationToken cancellationToken);
    Task<bool> RenewAsync(EvidenceProcessingClaim claim, TimeSpan leaseDuration, CancellationToken cancellationToken);
    Task<bool> WaitForPredecessorAsync(EvidenceProcessingClaim claim, string predecessorAttemptId,
        CancellationToken cancellationToken);
    /// <summary>
    /// Applies a lifecycle-valid checkpoint only while its fenced lease is current.
    /// Derives retry deadlines from RetryDelay using the same authoritative clock as claim eligibility.
    /// </summary>
    Task<bool> CheckpointAsync(EvidenceProcessingCheckpoint checkpoint, CancellationToken cancellationToken);
    Task<bool> ReleaseAsync(EvidenceProcessingClaim claim, CancellationToken cancellationToken);
}
