using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;

namespace CivicLens.Application.ExecutiveActions;

/// <summary>The identity of the executive action summary task. It follows <see cref="ActionDraftTask"/>, and it shares the model of that task.</summary>
public static class ExecutiveSummaryTask
{
    public const string Task = "executive-summary";
    public const string TaskVersion = "executive-summary-v1";
    public const string PromptVersion = "executive-summary-prompt-v2";
    public const string SchemaVersion = "executive-summary-schema-v1";
    public const string Model = ActionDraftTask.Model;
    public const int MaximumOutputTokens = 3_000;
    public const int MaximumPromptBytes = AnalysisRunInput.MaximumPromptBytes;

    /// <summary>The queue priority of a new entry. A document has no tracked issue and no statement to pair with, so it takes the lowest tier.</summary>
    public const int QueuePriority = 3;

    public static readonly AnalysisTaskIdentity Identity = new(Task, TaskVersion, Model, PromptVersion, SchemaVersion);
}
