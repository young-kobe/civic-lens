using CivicLens.Application.Analysis;

namespace CivicLens.Application.Actions;

public static class ActionDraftTask
{
    public const string Task = "action-draft";
    public const string TaskVersion = "action-draft-v1";
    public const string PromptVersion = "action-draft-prompt-v2";
    public const string SchemaVersion = "action-draft-schema-v2";
    public const string Model = "claude-haiku-5-5";
    public const string Effort = "medium";
    public const int MaximumOutputTokens = 4_000;
    public const int MaximumPromptBytes = 100_000;
    public const int MaximumValidationPasses = 2;
    public const int MaximumSummaryPages = 20;

    public static readonly AnalysisTaskIdentity Identity = new(Task, TaskVersion, Model, PromptVersion, SchemaVersion);
}
