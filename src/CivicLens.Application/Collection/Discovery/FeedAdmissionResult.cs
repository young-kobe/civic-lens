namespace CivicLens.Application.Collection.Discovery;

public sealed record FeedAdmissionResult(
    IReadOnlyList<FeedAdmissionJob> Jobs,
    int DeferredCount,
    int DuplicateCount)
{
    public int AdmittedCount => Jobs.Count;
}
