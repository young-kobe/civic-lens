namespace CivicLens.Infrastructure.Roster.Persistence;

internal sealed class ExecutiveOfficialRow
{
    public string AttemptId { get; set; } = null!;
    public int GovtrackId { get; set; }
    public string? WikidataId { get; set; }
    public string FullName { get; set; } = null!;
}
