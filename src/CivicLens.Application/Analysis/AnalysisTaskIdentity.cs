namespace CivicLens.Application.Analysis;

public sealed record AnalysisTaskIdentity(string Task, string TaskVersion, string Model, string PromptVersion, string SchemaVersion);
