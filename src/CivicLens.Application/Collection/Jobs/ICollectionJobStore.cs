using CivicLens.Application.Paging;

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
    /// <summary>Jobs newest first, ordered by creation time then job ID.</summary>
    Task<CollectionJobPage> ListPageAsync(PageCursor? cursor, int limit, CancellationToken cancellationToken);
    /// <summary>The newest job per target whose source ID and URL both match; targets with no job are omitted.</summary>
    Task<IReadOnlyList<CollectionJobRecord>> ListLatestBySourceAsync(IReadOnlyCollection<SourceCheckTarget> targets,
        CancellationToken cancellationToken);
    Task<CollectionJobActivity> ListActivityAsync(int limit, CancellationToken cancellationToken);
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
