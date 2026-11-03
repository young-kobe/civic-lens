namespace CivicLens.Application.Publication;

public sealed record ReleaseRetentionPlan(int HighestNumber, IReadOnlySet<int> KeptNumbers)
{
    // A folder above the highest committed number may belong to a release in progress, so it is never retired.
    public bool Retires(int releaseNumber) => releaseNumber <= HighestNumber && !KeptNumbers.Contains(releaseNumber);
}
