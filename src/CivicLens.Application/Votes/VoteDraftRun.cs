using CivicLens.Core.Analysis;

namespace CivicLens.Application.Votes;

public sealed record VoteDraftRun(AnalysisRun Run, string? Reason);
