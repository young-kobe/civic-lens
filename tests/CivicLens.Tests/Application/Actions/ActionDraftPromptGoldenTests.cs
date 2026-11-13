using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Application.Issues;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.ActionBillWorld;

namespace CivicLens.Tests.Application.Actions;

public sealed class ActionDraftPromptGoldenTests
{
    private const string SystemSha256 = "3192dcc0b23e5f26e97f18eb89d824a95108eeb0c849d29c688827fc0c3c57ed";
    private const string NoCandidateSystemSha256 = "7a0707c25bc6a39b7eeabdcf485f9cd6e4a92320b77bc9a47d92203858220515";
    private const string SchemaSha256 = "6d6adb018d7ee24f5df2e21a1f678cc87ed0fb7c144cf0a5c99cd84153a3acf0";
    private const string InputHash = "869e1469552eb5d2079fb6d97d3b9a9ce25829d3a71c1a3f5cd4ca38c6195844";

    [Fact]
    public async Task ThePromptHoldsTheSharedAiTextSentencesAndStatesTheCodeGradeLimit()
    {
        var world = Create();
        var snapshot = (await world.Reader().ReadAsync(world.Evidence(), default)).Snapshot!;

        AiTextPromptRules.AssertHoldsSharedSentences(ActionDraftPrompt.Build(snapshot, NoChoice(), ActionDraftTask.MaximumPromptBytes)!.SystemPrompt, codeApproves: true);
    }

    [Fact]
    public async Task ThePromptAndSchemaMatchTheirPinnedVersions()
    {
        var world = Create(cosponsorPages: [[new(Markey, "2025-01-04", Original: true)]],
            summaries: [new("00", "2025-01-03", "2025-01-05T10:00:00Z", "<p>It funds rent aid.</p>")]);
        var snapshot = (await world.Reader().ReadAsync(world.Evidence(), default)).Snapshot!;

        var request = ActionDraftPrompt.Build(snapshot, BillGateFixtures.HousingChoice(), ActionDraftTask.MaximumPromptBytes)!;

        var bump = $"The prompt or schema changed. Bump {nameof(ActionDraftTask.PromptVersion)} ({ActionDraftTask.PromptVersion}) or " +
            $"{nameof(ActionDraftTask.SchemaVersion)} ({ActionDraftTask.SchemaVersion}), and then update the pinned hashes in this test.";
        Assert.Equal(("action-draft", "action-draft-v1", "action-draft-prompt-v6", "action-draft-schema-v2", "claude-haiku-5-5"),
            (ActionDraftTask.Task, ActionDraftTask.TaskVersion, ActionDraftTask.PromptVersion, ActionDraftTask.SchemaVersion, ActionDraftTask.Model));
        Assert.NotEqual(StatementDraftTaskName, ActionDraftTask.Task);
        Assert.True(Sha256(request.SystemPrompt) == SystemSha256, $"{bump} System prompt: {Sha256(request.SystemPrompt)}");
        Assert.True(Sha256(request.SchemaJson) == SchemaSha256, $"{bump} Schema: {Sha256(request.SchemaJson)}");
        Assert.True(request.InputHash == InputHash, $"{bump} Input hash: {request.InputHash}");
    }

    [Fact]
    public async Task ThePublicTextRulesAreInTheCachedSystemPromptAndNoCodeSentenceIsAskedOfTheModel()
    {
        var world = Create();
        var snapshot = (await world.Reader().ReadAsync(world.Evidence(), default)).Snapshot!;

        var request = ActionDraftPrompt.Build(snapshot, NoChoice(), ActionDraftTask.MaximumPromptBytes)!;

        Assert.True(request.CacheSystemPrompt);
        Assert.Equal(NoCandidateSystemSha256, Sha256(request.SystemPrompt));
        Assert.Contains("Never use the words tracked, entry, or candidate", request.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Use those words only when the bill itself is about the thing they name.", request.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Never write that it passed, was enacted, became law, or is law", request.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Congress.gov has no summary of this bill yet.", request.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Rent aid", request.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSummaryReachesTheModelAsTextAloneWithNoActionDescriptionNoActionDateAndNoVersionCode()
    {
        var world = Create(summaries: [new("00", "2025-01-03", "2025-01-05T10:00:00Z", "<p>It funds rent aid.</p>")]);
        var snapshot = (await world.Reader().ReadAsync(world.Evidence(), default)).Snapshot!;

        var user = ActionDraftPrompt.Build(snapshot, NoChoice(), ActionDraftTask.MaximumPromptBytes)!.UserPrompt;

        using var facts = System.Text.Json.JsonDocument.Parse(user["Facts:\n".Length..]);
        var summary = facts.RootElement.GetProperty("summary");
        Assert.Equal(["text"], summary.EnumerateObject().Select(property => property.Name));
        Assert.Contains("It funds rent aid.", summary.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("actionDate", ActionDraftPrompt.SerializeContext(snapshot, NoChoice(), "issues"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARetryPromptCarriesTheRejectionReasonsAndTheSameFacts()
    {
        var world = Create();
        var snapshot = (await world.Reader().ReadAsync(world.Evidence(), default)).Snapshot!;

        var first = ActionDraftPrompt.Build(snapshot, NoChoice(), 100_000)!;
        var retry = ActionDraftPrompt.Build(snapshot, NoChoice(), 100_000, ["headline is required."])!;

        Assert.Contains("- headline is required.", retry.UserPrompt, StringComparison.Ordinal);
        Assert.StartsWith(first.UserPrompt, retry.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheMinimumReservationIsTheSystemPromptPlusTheOutputAllowanceAndSettingsRejectALowerRunLimit()
    {
        Assert.True(ActionDraftPrompt.MinimumReservation > ActionDraftTask.MaximumOutputTokens);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActionDraftSettings(ActionDraftPrompt.MinimumReservation - 1));
        Assert.Equal(ActionDraftPrompt.MinimumReservation - ActionDraftTask.MaximumOutputTokens, new ActionDraftSettings(ActionDraftPrompt.MinimumReservation).PromptByteCap);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task AnAskedIssueChoiceListsEachCandidateAndAPolicyAreaWithNoIssueAsksForNone()
    {
        var snapshot = (await Create().Reader().ReadAsync(Create().Evidence(), default)).Snapshot!;

        var asked = ActionDraftPrompt.Build(snapshot, BillGateFixtures.HousingChoice(), 100_000)!;
        var none = ActionDraftPrompt.Build(snapshot, NoChoice(), 100_000)!;

        Assert.Contains("\"issueIds\"", asked.SchemaJson, StringComparison.Ordinal);
        Assert.Contains("\"definition\": \"Rent costs.\"", asked.UserPrompt, StringComparison.Ordinal);
        Assert.Equal(AnalysisSchema.HeadlineAndSummary(), none.SchemaJson);
        Assert.DoesNotContain("issueIds", none.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubjectsReachTheModelAsFactsOnlyWhenTheBillHasThemAndACandidateIssueExists()
    {
        var withSubjects = Create(subjectPages: [["Housing finance and home ownership", "Rent and rental housing"]]);
        var snapshot = (await withSubjects.Reader().ReadAsync(withSubjects.Evidence(), default)).Snapshot!;
        var plain = Create();
        var plainSnapshot = (await plain.Reader().ReadAsync(plain.Evidence(), default)).Snapshot!;

        var asked = ActionDraftPrompt.Build(snapshot, ChoiceWith(snapshot.Subjects), ActionDraftTask.MaximumPromptBytes)!;
        var noCandidateIssue = ActionDraftPrompt.Build(snapshot, NoChoiceWith(snapshot.Subjects), ActionDraftTask.MaximumPromptBytes)!;
        var noSubjects = ActionDraftPrompt.Build(plainSnapshot, BillGateFixtures.HousingChoice(), ActionDraftTask.MaximumPromptBytes)!;

        Assert.Contains("\"Rent and rental housing\"", asked.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("A subject does not make an issue fit.", asked.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Do not use the wording of a subject in the headline or the summary.", asked.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("subjects", noCandidateIssue.UserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("subjects", noCandidateIssue.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("subjects", noSubjects.UserPrompt, StringComparison.Ordinal);
        Assert.Equal(SystemSha256, Sha256(noSubjects.SystemPrompt));
    }

    private static BillIssueChoice ChoiceWith(IEnumerable<string> subjects) =>
        BillIssueChoice.For(BillGateFixtures.TrackedPolicyArea, subjects, new DateOnly(2025, 3, 3), BillGateFixtures.Issues());

    private static BillIssueChoice NoChoiceWith(IEnumerable<string> subjects) =>
        BillIssueChoice.For(BillGateFixtures.UntrackedPolicyArea, subjects, new DateOnly(2025, 3, 3), BillGateFixtures.Issues());

    private static BillIssueChoice NoChoice() =>
        BillIssueChoice.For(BillGateFixtures.UntrackedPolicyArea, [], new DateOnly(2025, 3, 3), BillGateFixtures.Issues());

    private const string StatementDraftTaskName = "statement-draft";

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
