using CivicLens.Core.Analysis;

namespace CivicLens.Application.ExecutiveActions;

public sealed record ExecutiveSummaryRun(AnalysisRun Run, string? Reason);
