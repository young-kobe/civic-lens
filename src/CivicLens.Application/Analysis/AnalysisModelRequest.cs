namespace CivicLens.Application.Analysis;

public sealed record AnalysisModelRequest(string Model, string Effort, int MaximumOutputTokens, string SystemPrompt,
    string UserPrompt, string SchemaJson, string InputHash, long ReservedTokens,
    bool CacheSystemPrompt = false, string? PromptVersion = null, string? SchemaVersion = null)
{
    public static AnalysisModelRequest Create(string model, int maximumOutputTokens, string system, string user, string schema,
        long inputBytes, bool cacheSystemPrompt = false, string? promptVersion = null, string? schemaVersion = null)
    {
        var hash = AnalysisPromptHash.Compute(model, AnalysisEffort.Medium, maximumOutputTokens, system, user, schema);
        return new(model, AnalysisEffort.Medium, maximumOutputTokens, system, user, schema, hash,
            inputBytes + maximumOutputTokens, cacheSystemPrompt, promptVersion, schemaVersion);
    }
}
