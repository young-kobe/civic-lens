namespace CivicLens.Infrastructure.Collection.Discovery;

internal sealed class FeedDiscoveryRow
{
    public string AttemptId { get; set; } = null!;
    public string SourceId { get; set; } = null!;
    public string RequestJson { get; set; } = null!;
    public string DiscoveryJson { get; set; } = null!;
}
