namespace CivicLens.Application.Analysis;

public readonly record struct AnalysisCall(AnalysisModelResponse? Response, bool Progress);
