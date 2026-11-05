namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleaseOfficialRow
{
    public int ReleaseNumber { get; set; }
    public string OfficialId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? WikidataId { get; set; }
    public string? Website { get; set; }
}
