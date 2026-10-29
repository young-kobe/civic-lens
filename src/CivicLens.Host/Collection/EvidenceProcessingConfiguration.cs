using CivicLens.Application.Collection.Processing;

namespace CivicLens.Host.Collection;

internal static class EvidenceProcessingConfiguration
{
    internal static int ReadConcurrency() =>
        WorkerEnvironment.ReadWholeNumber("CIVIC_LENS_EVIDENCE_CONCURRENCY") is { } value
            ? EvidenceProcessingPolicy.ValidateConcurrency(value)
            : EvidenceProcessingPolicy.DefaultConcurrency;
}
