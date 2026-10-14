using CivicLens.Core.Analysis;

namespace CivicLens.Application.Actions;

public sealed record ActionDraftRun(AnalysisRun Run, string? Reason);
