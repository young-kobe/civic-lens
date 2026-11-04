namespace CivicLens.Infrastructure.Publication.Persistence;

internal abstract class PublishedEntryChildRow
{
    public string Kind { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public short Position { get; set; }
}
