namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class PublicationReleaseRow
{
    public int ReleaseNumber { get; set; }
    public string DirectoryName { get; set; } = string.Empty;
    public string ActorSubject { get; set; } = string.Empty;
    public long PublishedAtUtcTicks { get; set; }
    public int RecordCount { get; set; }
    public string RecordsJson { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
