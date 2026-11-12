namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleaseIssuePartyCountRow
{
    public int ReleaseNumber { get; set; }
    public string IssueId { get; set; } = string.Empty;
    public string Party { get; set; } = string.Empty;
    public int Bills { get; set; }
}
