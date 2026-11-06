namespace CivicLens.Application.Analysis;

public sealed record StaleAnalysisStage(string Name, IStaleAnalysisRequeue Store);
