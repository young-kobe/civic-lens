namespace CivicLens.Application.Analysis;

public sealed record AnalysisModelRequest(string Model, int MaximumOutputTokens, string SystemPrompt,
    string UserPrompt, string SchemaJson, string InputHash, long ReservedTokens,
    bool CacheSystemPrompt = false);
