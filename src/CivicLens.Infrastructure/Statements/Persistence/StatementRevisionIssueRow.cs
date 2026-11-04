namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementRevisionIssueRow : IStatementRevisionChildRow
{
    public string CandidateId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string IssueId { get; set; } = string.Empty;
    public short Position { get; set; }
}
