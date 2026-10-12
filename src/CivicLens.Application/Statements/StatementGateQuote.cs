namespace CivicLens.Application.Statements;

public sealed record StatementGateQuote(string IssueId, string ExtractionId, int Start, int Length);
