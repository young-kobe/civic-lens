using System.Collections.Immutable;

namespace CivicLens.Application.Collection.Jobs;

public sealed record CollectionWorkerQueuePage(ImmutableArray<CollectionWorkerQueueJob> Jobs, CollectionWorkerCursor? NextCursor);
