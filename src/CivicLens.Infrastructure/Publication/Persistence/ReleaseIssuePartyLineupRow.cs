namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleaseIssuePartyLineupRow
{
    public int ReleaseNumber { get; set; }
    public string IssueId { get; set; } = string.Empty;
    public int UnknownBills { get; set; }
}
