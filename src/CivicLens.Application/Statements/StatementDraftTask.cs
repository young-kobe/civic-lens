using CivicLens.Core.Analysis;
using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public static class StatementDraftTask
{
    public const string Task = "statement-draft";
    public const string TaskVersion = "statement-draft-v1";
    public const string PromptVersion = "statement-draft-prompt-v3";
    public const string SchemaVersion = "statement-draft-schema-v1";
    public const string Model = "claude-sonnet-5-5";
    public const int MaximumOutputTokens = 16_000;
    public const int MaximumPromptBytes = AnalysisRunInput.MaximumPromptBytes;

    public static readonly AnalysisTaskIdentity Identity = new(Task, TaskVersion, Model, PromptVersion, SchemaVersion);

    public static readonly AnalysisCurrentVersions CurrentVersions = AnalysisCurrentVersions.Of(Identity);
}
