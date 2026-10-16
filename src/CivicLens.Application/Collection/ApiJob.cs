using CivicLens.Application.Collection.Jobs;

namespace CivicLens.Application.Collection;

public sealed record ApiJob(string IdempotencyKey, CollectionJobDefinition Definition);
