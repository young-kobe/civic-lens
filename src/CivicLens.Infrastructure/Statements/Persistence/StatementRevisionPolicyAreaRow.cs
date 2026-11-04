namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementRevisionPolicyAreaRow : IStatementRevisionChildRow
{
    public string CandidateId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string PolicyAreaId { get; set; } = string.Empty;
    public short Position { get; set; }
}
