namespace CivicLens.Application.Collection.Jobs;

public sealed record CollectionJobRenewal(bool Owned, bool CancellationRequested,
    CollectionJobLease? JobLease, CollectionCollectorLease? CollectorLease);
