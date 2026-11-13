using CivicLens.Core.Analysis;
using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public static class StatementGateTask
{
    public const string Task = "gate";
    public const string TaskVersion = "statement-gate-v1";
    public const string PromptVersion = "statement-gate-prompt-v1";
    public const string SchemaVersion = "statement-gate-schema-v1";
    public const string Model = "claude-haiku-5-5";
    public const int MaximumOutputTokens = 4_000;
    public const int MaximumPromptBytes = AnalysisRunInput.MaximumPromptBytes;
    public const int MaximumQuoteLength = 2_000;

    public static readonly AnalysisTaskIdentity Identity = new(Task, TaskVersion, Model, PromptVersion, SchemaVersion);
}
