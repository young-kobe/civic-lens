namespace CivicLens.Infrastructure.Roster.Persistence;

internal sealed class RosterMemberRow
{
    public string AttemptId { get; set; } = null!;
    public string BioguideId { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string? LastName { get; set; }
    public string? Office { get; set; }
    public string? Phone { get; set; }
    public string? Url { get; set; }
}
