using System.Collections.Immutable;

namespace CivicLens.Application.Collection.Jobs;

public sealed record CollectionWorkerQueuePage(ImmutableArray<string> JobIds, CollectionWorkerCursor? NextCursor);
