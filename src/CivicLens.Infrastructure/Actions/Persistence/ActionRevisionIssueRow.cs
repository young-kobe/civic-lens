namespace CivicLens.Infrastructure.Actions.Persistence;

internal sealed class ActionRevisionIssueRow
{
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string IssueId { get; set; } = string.Empty;
    public short Position { get; set; }
    public bool IsTracked { get; set; }
}
