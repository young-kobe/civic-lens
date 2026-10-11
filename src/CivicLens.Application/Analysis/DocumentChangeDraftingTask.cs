namespace CivicLens.Application.Analysis;

public static class DocumentChangeDraftingTask
{
    public const string Task = "draft";
    public const string TaskVersion = "document-change-draft-v1";
    public const string PromptVersion = "document-change-draft-prompt-v1";
    public const string SchemaVersion = "document-change-draft-schema-v1";
    public const string Model = "claude-haiku-5-5";
    public const string Effort = "medium";
    public const int MaximumOutputTokens = 16_000;
    public const int MaximumHunks = 64;
    public const int MaximumPromptBytes = 100_000;
    public const int MaximumValidationPasses = 2;
}
