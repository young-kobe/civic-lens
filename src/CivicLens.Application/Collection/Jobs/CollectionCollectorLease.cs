namespace CivicLens.Application.Collection.Jobs;

public sealed record CollectionCollectorLease(string Token, long Fence, DateTimeOffset ExpiresAt);
