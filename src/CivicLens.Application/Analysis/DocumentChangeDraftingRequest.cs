namespace CivicLens.Application.Analysis;

public sealed record DocumentChangeDraftingRequest(string Model, int MaximumOutputTokens, string SystemPrompt,
    string UserPrompt, string SchemaJson, string InputHash, long ReservedTokens);
