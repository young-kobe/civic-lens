namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleasePolicyAreaRow
{
    public int ReleaseNumber { get; set; }
    public string PolicyAreaId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
