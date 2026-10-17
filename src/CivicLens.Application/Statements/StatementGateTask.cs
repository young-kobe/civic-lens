using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public static class StatementGateTask
{
    public const string Task = "gate";
    public const string TaskVersion = "statement-gate-v2";
    public const string PromptVersion = "statement-gate-prompt-v2";
    public const string SchemaVersion = "statement-gate-schema-v2";
    public const string Model = "claude-haiku-5-5";
    public const string Effort = "medium";
    public const int MaximumOutputTokens = 4_000;
    public const int MaximumPromptBytes = 100_000;
    public const int MaximumValidationPasses = 2;
    public const int MaximumQuoteLength = 2_000;

    public static readonly AnalysisTaskIdentity Identity = new(Task, TaskVersion, Model, PromptVersion, SchemaVersion);
}
