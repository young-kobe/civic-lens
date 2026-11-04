namespace CivicLens.Infrastructure.Review.Persistence;

internal sealed class DocumentChangeRevisionIssueRow
{
    public string DraftId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string IssueId { get; set; } = string.Empty;
    public short Position { get; set; }
}
