namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleaseBillStatusRow
{
    public int ReleaseNumber { get; set; }
    public string RecordId { get; set; } = string.Empty;
    public string LatestActionText { get; set; } = string.Empty;
    public DateOnly LatestActionDate { get; set; }
    public long ReadAtUtcTicks { get; set; }
}
