using CivicLens.Application.Analysis;

namespace CivicLens.Application.Votes;

public sealed record VoteDraftListItem(string CandidateId, string SourceId, string RollCall, string? Bill, AnalysisState Status,
    VoteDraftView View, string? Code, int Attempts, string? LatestRunId);
