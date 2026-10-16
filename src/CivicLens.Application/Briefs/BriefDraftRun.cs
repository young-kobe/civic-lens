using CivicLens.Core.Analysis;

namespace CivicLens.Application.Briefs;

public sealed record BriefDraftRun(AnalysisRun Run, string? Reason);
