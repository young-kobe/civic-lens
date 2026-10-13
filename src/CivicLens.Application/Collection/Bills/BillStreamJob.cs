using CivicLens.Application.Collection.Jobs;

namespace CivicLens.Application.Collection.Bills;

public sealed record BillStreamJob(string IdempotencyKey, CollectionJobDefinition Definition);
