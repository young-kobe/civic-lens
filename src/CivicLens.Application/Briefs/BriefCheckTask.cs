using CivicLens.Core.Analysis;
using CivicLens.Application.Analysis;

namespace CivicLens.Application.Briefs;

public static class BriefCheckTask
{
    public const string Task = "brief-check";
    public const string TaskVersion = "brief-check-v1";
    public const string PromptVersion = "brief-check-prompt-v2";
    public const string SchemaVersion = "brief-check-schema-v1";
    public const string Model = "claude-sonnet-5-5";
    public const int MaximumOutputTokens = 4_000;
    public const int MaximumPromptBytes = AnalysisRunInput.MaximumPromptBytes;
    public const int MaximumValidationPasses = 2;

    public static readonly AnalysisTaskIdentity Identity = new(Task, TaskVersion, Model, PromptVersion, SchemaVersion);
}
