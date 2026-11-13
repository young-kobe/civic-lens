using CivicLens.Core.Analysis;
using CivicLens.Application.Analysis;

namespace CivicLens.Application.Votes;

public static class VoteDraftTask
{
    public const string Task = "vote-draft";
    public const string TaskVersion = "vote-draft-v1";
    public const string PromptVersion = "vote-draft-prompt-v5";
    public const string SchemaVersion = "vote-draft-schema-v2";
    public const string Model = "claude-haiku-5-5";
    public const int MaximumOutputTokens = 2_000;
    public const int MaximumPromptBytes = AnalysisRunInput.MaximumPromptBytes;

    public static readonly AnalysisTaskIdentity Identity = new(Task, TaskVersion, Model, PromptVersion, SchemaVersion);

    public static readonly AnalysisCurrentVersions CurrentVersions = AnalysisCurrentVersions.Of(Identity);
}
