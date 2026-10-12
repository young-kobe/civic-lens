namespace CivicLens.Application.Analysis;

public sealed record AnalysisClaim(AnalysisRecord Record, string LeaseToken, long Fence);
