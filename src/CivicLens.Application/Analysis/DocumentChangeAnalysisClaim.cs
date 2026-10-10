namespace CivicLens.Application.Analysis;

public sealed record DocumentChangeAnalysisClaim(DocumentChangeAnalysisRecord Record, string LeaseToken, long Fence);
