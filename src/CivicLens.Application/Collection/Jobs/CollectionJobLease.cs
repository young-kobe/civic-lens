namespace CivicLens.Application.Collection.Jobs;

public sealed record CollectionJobLease(string JobId, string Token, long Fence, DateTimeOffset ExpiresAt);
