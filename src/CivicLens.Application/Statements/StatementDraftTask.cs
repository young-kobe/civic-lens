using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public static class StatementDraftTask
{
    public const string Task = "draft";
    public const string StatusTask = "statement-draft";
    public const string TaskVersion = "statement-draft-v1";
    public const string PromptVersion = "statement-draft-prompt-v1";
    public const string SchemaVersion = "statement-draft-schema-v1";
    public const string Model = "claude-sonnet-5-5";
    public const string Effort = "medium";
    public const int MaximumOutputTokens = 16_000;
    public const int MaximumPromptBytes = 100_000;
    public const int MaximumValidationPasses = 2;

    public static readonly AnalysisTaskIdentity Identity = new(Task, TaskVersion, Model, PromptVersion, SchemaVersion);
}
