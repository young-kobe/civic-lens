namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleaseCorrectionHistoryRow
{
    public int ReleaseNumber { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string HistoryId { get; set; } = string.Empty;
}
