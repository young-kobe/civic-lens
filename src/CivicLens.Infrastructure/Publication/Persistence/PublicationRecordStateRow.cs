namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class PublicationRecordStateRow
{
    public string Kind { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public int FirstRelease { get; set; }
    public int? LastRelease { get; set; }
}
