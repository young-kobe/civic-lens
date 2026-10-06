namespace CivicLens.Application.Collection.Jobs;

public sealed record CollectionJobClaim(CollectionJobLease Lease, CollectionJobRecord Job);
