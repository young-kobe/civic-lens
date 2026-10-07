namespace CivicLens.Application.Collection.Jobs;

public interface ICollectionJobStore
{
    /// <summary>Resolve a configured request after atomically reading its key; only new keys evaluate current eligibility.</summary>
    Task<CollectionJobRecord> EnqueueAsync(ConfiguredCollectionSource source, string idempotencyKey,
        CancellationToken cancellationToken);
    Task<CollectionJobRecord> EnqueueAsync(CollectionJobDefinition definition, string idempotencyKey,
        CancellationToken cancellationToken);
    Task<CollectionJobRecord?> GetAsync(string jobId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CollectionJobRecord>> ListAsync(int limit, CancellationToken cancellationToken);
    Task<CollectionJobRecord?> CancelAsync(string jobId, CancellationToken cancellationToken);
    Task<CollectionJobClaim?> TryClaimAsync(string jobId, TimeSpan leaseDuration, CancellationToken cancellationToken);
    Task<CollectionJobRenewal> RenewAsync(CollectionJobLease jobLease, CollectionCollectorLease? collectorLease,
        TimeSpan leaseDuration, CancellationToken cancellationToken);
    Task<CollectionJobStartResult> TryStartAttemptAsync(CollectionJobLease jobLease, string artifactDirectory,
        CancellationToken cancellationToken);
    Task<bool> ReleaseCollectorAsync(CollectionJobLease jobLease, CollectionCollectorLease collectorLease,
        CancellationToken cancellationToken);
    Task<bool> SettleAttemptAsync(CollectionJobLease jobLease, string attemptId,
        CollectionAttemptResolution resolution, CancellationToken cancellationToken);
    Task<bool> ReleaseClaimAsync(CollectionJobLease jobLease, CancellationToken cancellationToken);
}
