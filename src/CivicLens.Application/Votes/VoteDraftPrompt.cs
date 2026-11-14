using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Application.Issues;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public static class VoteDraftPrompt
{
    private static readonly string SystemInstructions = $"""
        You choose which candidate issues the measure of one roll-call vote of the United States Congress is about, for Civic Lens, a nonpartisan transparency publication. The user message holds the lines of the vote record, the candidate issues, and sometimes the subjects of the bill. {AiTextRules.UntrustedSource("The text of the lines and the subjects")} Answer with the issueIds and a confidence. The confidence is your probability that a careful human reviewer would approve the issue choice. {AiTextRules.ConfidenceScale}

        Issues:
        - issueIds: the ids of the candidate issues that the measure is about. Choose from the candidate issues only. List each id once. The policy area of the bill is a hint only: never choose an issue because of the policy area. Choose an issue only when words in the title or the subjects show that the measure is about it. When no such words exist, use an empty list. Read the definition, the includes, and the excludes of each candidate.
        """;

    public static long MinimumReservation => Encoding.UTF8.GetByteCount(SystemInstructions) + VoteDraftTask.MaximumOutputTokens;

    /// <summary>The issue-only request. Code writes the wording, so the model only chooses issues, and only when the bill has candidates.</summary>
    public static AnalysisModelRequest? Build(VoteSnapshot snapshot, BillIssueChoice issues, int maximumBytes,
        ImmutableArray<string> previousErrors = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(issues);
        var user = BuildUserPrompt(snapshot, issues, previousErrors);
        var schema = AnalysisSchema.IssueChoice(issues.AnswerSchema() ?? throw new ArgumentException("An issue request needs candidate issues.", nameof(issues)));
        var bytes = Encoding.UTF8.GetByteCount(SystemInstructions) + Encoding.UTF8.GetByteCount(user) + Encoding.UTF8.GetByteCount(schema);
        if (bytes > maximumBytes) return null;
        return AnalysisModelRequest.Create(VoteDraftTask.Model, VoteDraftTask.MaximumOutputTokens, SystemInstructions, user, schema, bytes, cacheSystemPrompt: true);
    }

    public static string SerializeContext(VoteSnapshot snapshot, BillIssueChoice issues, string issueRevisionId) =>
        JsonSerializer.Serialize(new
        {
            officials = snapshot.Members.Select(item => new { id = item.PersonId, name = snapshot.OfficialNames[item.PersonId] }),
            issueCandidateIds = issues.CandidateIds,
            issueRevisionId,
            fingerprint = snapshot.Fingerprint
        }, AnalysisPromptText.CompactJson);

    private static string BuildUserPrompt(VoteSnapshot snapshot, BillIssueChoice issues, ImmutableArray<string> previousErrors)
    {
        var document = new
        {
            lines = snapshot.Facts.Select(fact => fact.Text),
            candidateIssues = issues.PromptSection(),
            subjects = issues.SubjectsSection()
        };
        var builder = new StringBuilder("Facts:\n").Append(JsonSerializer.Serialize(document, AnalysisPromptText.EvidenceJson));
        return AnalysisPromptText.WithRejectionReasons(builder, previousErrors);
    }
}
