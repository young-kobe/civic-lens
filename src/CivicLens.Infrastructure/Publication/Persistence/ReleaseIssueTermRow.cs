namespace CivicLens.Infrastructure.Publication.Persistence;

internal abstract class ReleaseIssueTermRow
{
    public int ReleaseNumber { get; set; }
    public string IssueId { get; set; } = string.Empty;
    public short Position { get; set; }
    public string Text { get; set; } = string.Empty;
}
