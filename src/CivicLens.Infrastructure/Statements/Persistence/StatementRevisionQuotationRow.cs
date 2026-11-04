using CivicLens.Core.Statements;

namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementRevisionQuotationRow : IStatementRevisionChildRow
{
    public string CandidateId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public short Position { get; set; }
    public string QuotationExtractionId { get; set; } = string.Empty;
    public int QuotationStart { get; set; }
    public int QuotationLength { get; set; }
    public StatementQuotationKind Kind { get; set; }
}
