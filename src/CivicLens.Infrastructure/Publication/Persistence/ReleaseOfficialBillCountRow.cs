namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleaseOfficialBillCountRow
{
    public int ReleaseNumber { get; set; }
    public int Congress { get; set; }
    public string OfficialId { get; set; } = string.Empty;
    public int Sponsored { get; set; }
    public int Cosponsored { get; set; }
}
