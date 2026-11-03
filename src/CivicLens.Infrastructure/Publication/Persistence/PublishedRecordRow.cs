namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class PublishedRecordRow
{
    public string Kind { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
    public long FirstPublishedAtUtcTicks { get; set; }
}
