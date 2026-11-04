namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementRevisionBillNumberRow : IStatementRevisionChildRow
{
    public string CandidateId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public short Position { get; set; }
    public string BillNumber { get; set; } = string.Empty;
}
