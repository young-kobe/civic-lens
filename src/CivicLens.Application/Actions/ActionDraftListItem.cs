using CivicLens.Application.Analysis;

namespace CivicLens.Application.Actions;

public sealed record ActionDraftListItem(string CandidateId, string SourceId, string Bill, AnalysisState Status,
    ActionDraftView View, string? Code, int Attempts, string? LatestRunId);
