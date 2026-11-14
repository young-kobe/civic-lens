using System.Collections.Immutable;
using System.Text;
using CivicLens.Application.Analysis;
using CivicLens.Application.Issues;

namespace CivicLens.Application.Actions;

public static class ActionIssuePrompt
{
    private static readonly string SystemInstructions = $"""
        You choose which candidate issues a bill of the United States Congress is about, for Civic Lens, a nonpartisan transparency publication. The user message holds facts from Congress.gov: the bill, its listed sponsors, the candidate issues, and sometimes subjects. {AiTextRules.UntrustedSource("The text of the facts")} Answer with the issueIds and a confidence. The confidence is your probability that a careful human reviewer would approve the issue choice. {AiTextRules.ConfidenceScale}

        Issues:
        - issueIds: the ids of the candidate issues that the bill is about. Choose from the candidate issues only. List each id once. The policy area of the bill is a hint only: never choose an issue because of the policy area. Choose an issue only when words in the title or the subjects show that the bill is about it. When no such words exist, use an empty list. Read the definition, the includes, and the excludes of each candidate.
        """;

    public static AnalysisModelRequest? Build(ActionBillSnapshot snapshot, BillIssueChoice issues, int maximumBytes,
        ImmutableArray<string> previousErrors = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(issues);
        var user = ActionDraftPrompt.UserPrompt(snapshot, issues, previousErrors, withSummary: false, billText: null);
        var schema = AnalysisSchema.IssueChoice(issues.AnswerSchema()!);
        var bytes = Encoding.UTF8.GetByteCount(SystemInstructions) + Encoding.UTF8.GetByteCount(user) + Encoding.UTF8.GetByteCount(schema);
        if (bytes > maximumBytes) return null;
        return AnalysisModelRequest.Create(ActionDraftTask.Model, ActionDraftTask.MaximumIssueOutputTokens, SystemInstructions, user, schema, bytes,
            cacheSystemPrompt: true, promptVersion: ActionDraftTask.IssuePromptVersion, schemaVersion: ActionDraftTask.IssueSchemaVersion);
    }
}
