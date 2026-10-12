using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public sealed record StatementGateListItem(string CandidateId, string SourceId, string Url, AnalysisState Status,
    StatementGateView View, string? Code, int Attempts, string? LatestRunId, string? IssueRevisionId);
