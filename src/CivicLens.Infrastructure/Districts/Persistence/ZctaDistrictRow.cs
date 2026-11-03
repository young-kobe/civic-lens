namespace CivicLens.Infrastructure.Districts.Persistence;

internal sealed class ZctaDistrictRow
{
    public string AttemptId { get; set; } = null!;
    public string Zcta { get; set; } = null!;
    public string State { get; set; } = null!;
    public int District { get; set; }
    public long LandAreaSquareMeters { get; set; }
}
