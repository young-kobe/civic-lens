namespace CivicLens.Infrastructure.Collection.Discovery;

internal sealed class DiscoveryRow
{
    public string AttemptId { get; set; } = null!;
    public string SourceId { get; set; } = null!;
    public string RequestJson { get; set; } = null!;
    public string DiscoveryJson { get; set; } = null!;
}
