namespace CivicLens.Infrastructure.Votes.Persistence;

internal sealed class VoteRevisionIssueRow
{
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string IssueId { get; set; } = string.Empty;
    public short Position { get; set; }
    public bool IsTracked { get; set; }
}
