using CivicLens.Application.Analysis;

namespace CivicLens.Application.Relationships;

public static class RelationshipDraftTask
{
    public const string Task = "relationship-draft";
    public const string TaskVersion = "relationship-draft-v1";
    public const string PromptVersion = "relationship-draft-prompt-v1";
    public const string SchemaVersion = "relationship-draft-schema-v1";
    public const string Model = "claude-haiku-5-5";
    public const string Effort = "medium";
    public const int MaximumOutputTokens = 1_000;
    public const int MaximumPromptBytes = 100_000;
    public const int MaximumValidationPasses = 2;

    public static readonly AnalysisTaskIdentity Identity = new(Task, TaskVersion, Model, PromptVersion, SchemaVersion);
}
