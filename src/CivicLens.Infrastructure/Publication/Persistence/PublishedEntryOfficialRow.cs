namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class PublishedEntryOfficialRow : PublishedEntryChildRow
{
    public string OfficialId { get; set; } = string.Empty;
    public bool IsMatched { get; set; }
}
