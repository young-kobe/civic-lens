namespace CivicLens.Infrastructure.Collection.Discovery;

internal sealed class DiscoveryCandidateJobRow
{
    public string SourceId { get; set; } = null!;
    public string CandidateHash { get; set; } = null!;
    public string Url { get; set; } = null!;
    public string JobId { get; set; } = null!;
}
