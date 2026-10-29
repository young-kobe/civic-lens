namespace CivicLens.Application.Collection.Jobs;

/// <summary>Reads bounded, database-time eligible jobs with their pacing keys without acquiring their leases.</summary>
public interface ICollectionWorkerQueue
{
    Task<CollectionWorkerQueuePage> GetEligibleJobsAsync(CollectionWorkerCursor? cursor, int limit,
        CancellationToken cancellationToken);
}
