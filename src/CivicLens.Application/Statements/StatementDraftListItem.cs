using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public sealed record StatementDraftListItem(string CandidateId, string SourceId, string Url, AnalysisState Status,
    StatementDraftView View, string? Code, int Attempts, string? LatestRunId);
