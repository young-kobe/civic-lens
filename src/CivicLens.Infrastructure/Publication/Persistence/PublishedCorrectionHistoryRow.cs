namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class PublishedCorrectionHistoryRow
{
    public string HistoryId { get; set; } = string.Empty;
    public int EncodingVersion { get; set; }
    public short CorrectionCount { get; set; }
}
