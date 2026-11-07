namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleaseBillHistoryRow
{
    public int ReleaseNumber { get; set; }
    public string RecordId { get; set; } = string.Empty;
    public string DetailJobId { get; set; } = string.Empty;
}
