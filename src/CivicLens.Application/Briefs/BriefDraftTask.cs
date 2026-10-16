using CivicLens.Application.Analysis;

namespace CivicLens.Application.Briefs;

public static class BriefDraftTask
{
    public const string Task = "brief-draft";
    public const string TaskVersion = "brief-draft-v1";
    public const string PromptVersion = "brief-draft-prompt-v1";
    public const string SchemaVersion = "brief-draft-schema-v1";
    public const string Model = "claude-sonnet-5-5";
    public const string Effort = "medium";
    public const int MaximumOutputTokens = 8_000;
    public const int MaximumPromptBytes = 100_000;
    public const int MaximumValidationPasses = 2;
    public const int RetryReasonReserveBytes = 4_096;

    public static readonly AnalysisTaskIdentity Identity = new(Task, TaskVersion, Model, PromptVersion, SchemaVersion);
}
