namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleaseIssueRow
{
    public int ReleaseNumber { get; set; }
    public string IssueId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Definition { get; set; }
}
