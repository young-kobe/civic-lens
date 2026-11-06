using CivicLens.Core.Analysis;

namespace CivicLens.Application.Analysis;

/// <summary>One model call of a measured record, with its run outcome and the answer as the model wrote it.</summary>
public sealed record AnalysisMeasurePass(AnalysisRunOutcome Outcome, string? OutputJson);
