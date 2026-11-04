namespace CivicLens.Application.Analysis;

public sealed record AnalysisModelRequest(string Model, string Effort, int MaximumOutputTokens, string SystemPrompt,
    string UserPrompt, string SchemaJson, string InputHash, long ReservedTokens,
    bool CacheSystemPrompt = false, string? PromptVersion = null, string? SchemaVersion = null);
