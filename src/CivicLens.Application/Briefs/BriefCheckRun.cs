using CivicLens.Core.Analysis;

namespace CivicLens.Application.Briefs;

public sealed record BriefCheckRun(AnalysisRun Run, string? Reason, BriefCheckResult? Result, string? SourceRunId);
