namespace CivicLens.Application.Collection.Discovery;

public sealed record DiscoveryAdmissionResult(
    IReadOnlyList<DiscoveryAdmissionJob> Jobs,
    int DeferredCount,
    int DuplicateCount)
{
    public int AdmittedCount => Jobs.Count;
}
