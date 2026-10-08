namespace CivicLens.Application.Collection.Jobs;

/// <summary>Reads bounded, database-time eligible job IDs without acquiring their leases.</summary>
public interface ICollectionWorkerQueue
{
    Task<CollectionWorkerQueuePage> GetEligibleJobIdsAsync(CollectionWorkerCursor? cursor, int limit,
        CancellationToken cancellationToken);
}
