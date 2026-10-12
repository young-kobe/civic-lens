namespace CivicLens.Application.Statements;

public sealed record StatementAnalysisClaim(StatementAnalysisRecord Record, string LeaseToken, long Fence);
