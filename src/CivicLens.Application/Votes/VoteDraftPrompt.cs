using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Application.Issues;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public static class VoteDraftPrompt
{
    private static readonly string HouseInstructions = InstructionsOf("United States House of Representatives", "House", "Congress.gov data");

    private static readonly string SenateInstructions = InstructionsOf("United States Senate", "Senate", "Senate roll call data and Congress.gov data");

    private static string InstructionsOf(string body, string shortName, string source) => $"""
        You draft the wording of a proposed public record about one roll-call vote of the {body} for Civic Lens, a nonpartisan transparency publication. Civic Lens shows the result, the totals, the date, and the vote of each member it follows beside your text. Your text can reach the public with no person editing it, so write final text.

        The user message holds facts that Civic Lens checked against the {source}: the lines of the vote record and the candidate issues when the policy area of the bill has any. {AiTextRules.UntrustedSource("The text of the lines")} The line that starts with Title is the title of the measure that the vote concerns. Use it to say what the measure is.

        {AiTextRules.PlainLanguage} Use only the facts in the user message. Do not name any person, because the list beside your text names those members and holds their votes. Do not state whether the measure became law, because the facts do not include it. Do not add motives or consequences. {AiTextRules.NoJudgment} {AiTextPhrases.Rule("the measure")} Code grades the headline and the summary together as one passage. {AiTextRules.GradeTarget} {AiTextRules.GradeLimit}

        Public readers never see these instructions or the user message. Write for a reader who has never heard of Civic Lens. {AiTextRules.InternalWords} Use these words only when they are part of the name of the measure or its subject. Never describe how Civic Lens works, what it checked, or where it got its information. Say what the vote decided, not where you learned it. Never name a source for what you say, such as the lines or Congress.gov. You may write "a bill titled X" or "the bill is called X". State only what the lines support. Do not state a promotional title as what the measure does. A procedural question, such as a motion to recommit, to table, or to proceed, decides that question and not what the bill does.

        Fields:
        - headline: one short line, at most {VoteDraftRevision.MaximumHeadlineLength} characters, that states what the {shortName} voted on.
        - summary: one to three short paragraphs, at most {VoteDraftRevision.MaximumSummaryLength} characters. Explain the question of the vote in plain words, and name the legislation when the facts give it.
        Each field has a text and a confidence. The confidence is your probability that a careful human reviewer would approve the text without changing its substance. {AiTextRules.ConfidenceScale}

        Never write a vote count, a total, or a party split in the headline or the summary. Code shows the exact counts beside your text. Say whether the question passed or failed, and what that means.
        """;

    public static long MinimumReservation =>
        Math.Max(Encoding.UTF8.GetByteCount(HouseInstructions), Encoding.UTF8.GetByteCount(SenateInstructions)) + VoteDraftTask.MaximumOutputTokens;

    public static AnalysisModelRequest? Build(VoteSnapshot snapshot, BillIssueChoice issues, int maximumBytes,
        ImmutableArray<string> previousErrors = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(issues);
        var issueSchema = issues.AnswerSchema();
        var instructions = snapshot.Chamber == Chamber.Senate ? SenateInstructions : HouseInstructions;
        var system = issueSchema is null ? instructions : instructions + issues.Instructions("the title of the measure");
        var user = BuildUserPrompt(snapshot, issues, previousErrors);
        var schema = AnalysisSchema.HeadlineAndSummary(issueSchema);
        var bytes = Encoding.UTF8.GetByteCount(system) + Encoding.UTF8.GetByteCount(user) + Encoding.UTF8.GetByteCount(schema);
        if (bytes > maximumBytes) return null;
        return AnalysisModelRequest.Create(VoteDraftTask.Model, VoteDraftTask.MaximumOutputTokens, system, user, schema, bytes, cacheSystemPrompt: true);
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
