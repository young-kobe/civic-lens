using CivicLens.Application.Collection.Jobs;

namespace CivicLens.Host.Collection;

internal static class CollectionLaneConfiguration
{
    internal static int ReadConcurrency() =>
        WorkerEnvironment.ReadWholeNumber("CIVIC_LENS_COLLECTION_CONCURRENCY") is { } value
            ? CollectionLanePolicy.ValidateConcurrency(value)
            : CollectionLanePolicy.DefaultConcurrency;
}
