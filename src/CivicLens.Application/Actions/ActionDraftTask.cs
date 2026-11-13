using CivicLens.Core.Analysis;
using CivicLens.Application.Analysis;

namespace CivicLens.Application.Actions;

public static class ActionDraftTask
{
    public const string Task = "action-draft";
    public const string TaskVersion = "action-draft-v1";
    public const string PromptVersion = "action-draft-prompt-v6";
    public const string SchemaVersion = "action-draft-schema-v2";
    public const string IssuePromptVersion = "action-issue-prompt-v4";
    public const string IssueSchemaVersion = "action-issue-schema-v1";
    public const string TextPromptVersion = "action-text-prompt-v8";
    public const string TextSchemaVersion = "action-text-schema-v1";

    /// <summary>The versions of a text prompt that holds the key term part. Only that prompt is sent with them. A bill with no definitions section is still sent v8.</summary>
    public const string KeyTermTextPromptVersion = "action-text-prompt-v9";
    public const string KeyTermTextSchemaVersion = "action-text-schema-v2";
    public const string Model = "claude-haiku-5-5";
    public const int MaximumOutputTokens = 4_000;
    public const int MaximumTextOutputTokens = 16_000;
    public const int MaximumIssueOutputTokens = 3_000;
    public const int MaximumPromptBytes = AnalysisRunInput.MaximumPromptBytes;
    public const int MaximumSummaryPages = 20;

    /// <summary>The current versions for the key term setting. With the setting off, only the text prompt without key terms is current. With it on, both
    /// text prompts are current, because a bill with no definitions section is sent v8 and a redraft would send it again and pay again.
    /// A draft from before the setting gets key terms only when its bill next changes.</summary>
    public static AnalysisCurrentVersions CurrentVersions(bool draftsKeyTerms) => draftsKeyTerms ? KeyTermVersions : PlainVersions;

    private static readonly AnalysisCurrentVersions PlainVersions = new(TaskVersion,
        [PromptVersion, IssuePromptVersion, TextPromptVersion], [SchemaVersion, IssueSchemaVersion, TextSchemaVersion]);

    private static readonly AnalysisCurrentVersions KeyTermVersions = new(TaskVersion,
        [PromptVersion, IssuePromptVersion, TextPromptVersion, KeyTermTextPromptVersion],
        [SchemaVersion, IssueSchemaVersion, TextSchemaVersion, KeyTermTextSchemaVersion]);

    public static readonly AnalysisTaskIdentity Identity = new(Task, TaskVersion, Model, PromptVersion, SchemaVersion);
}
