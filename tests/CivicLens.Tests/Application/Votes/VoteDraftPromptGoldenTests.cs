using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Analysis;
using CivicLens.Application.Issues;
using CivicLens.Application.Votes;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Votes;

public sealed class VoteDraftPromptGoldenTests
{
    private const string SystemSha256 = "245b5d6f8ab50710082f64d673c9a6665f308595983b7151bedae03a271da2e4";
    private const string SenateSystemSha256 = "2cc0614e3d80c6395ab0b403f591b76103e1758cbacf2fd9d6e9e2357ec98708";
    private const string SchemaSha256 = "6d6adb018d7ee24f5df2e21a1f678cc87ed0fb7c144cf0a5c99cd84153a3acf0";
    private const string InputHash = "c855c5605249f2b1cf46bd16f675a26df9ae9717b838867d5f7c6c5ee9c5f2b4";

    [Fact]
    public async Task ThePromptAndSchemaMatchTheirPinnedVersions()
    {
        var world = VoteWorld.Create();
        var snapshot = (await world.Reader().ReadAsync(world.Evidence(), default)).Snapshot!;

        var request = VoteDraftPrompt.Build(snapshot, BillGateFixtures.HousingChoice(), VoteDraftTask.MaximumPromptBytes)!;

        var bump = $"The prompt or schema changed. Bump {nameof(VoteDraftTask.PromptVersion)} ({VoteDraftTask.PromptVersion}) or " +
            $"{nameof(VoteDraftTask.SchemaVersion)} ({VoteDraftTask.SchemaVersion}), and then update the pinned hashes in this test.";
        Assert.Equal(("vote-draft", "vote-draft-v1", "vote-draft-prompt-v5", "vote-draft-schema-v2", "claude-haiku-5-5"),
            (VoteDraftTask.Task, VoteDraftTask.TaskVersion, VoteDraftTask.PromptVersion, VoteDraftTask.SchemaVersion, VoteDraftTask.Model));
        Assert.True(Sha256(request.SystemPrompt) == SystemSha256, $"{bump} System prompt: {Sha256(request.SystemPrompt)}");
        Assert.True(Sha256(request.SchemaJson) == SchemaSha256, $"{bump} Schema: {Sha256(request.SchemaJson)}");
        Assert.True(request.InputHash == InputHash, $"{bump} Input hash: {request.InputHash}");
    }

    [Fact]
    public async Task TheSenatePromptNamesTheSenateAndItsSourcesWhileTheHouseGoldenAbovePinsTheHouseBytes()
    {
        var world = VoteviewVoteWorld.Create();
        var snapshot = (await world.Reader().ReadAsync(world.Evidence(), default)).Snapshot!;

        var request = VoteDraftPrompt.Build(snapshot, BillGateFixtures.HousingChoice(), VoteDraftTask.MaximumPromptBytes)!;

        var bump = $"The Senate prompt changed. Bump {nameof(VoteDraftTask.PromptVersion)} ({VoteDraftTask.PromptVersion}), and then update the pinned hash in this test.";
        Assert.True(Sha256(request.SystemPrompt) == SenateSystemSha256, $"{bump} System prompt: {Sha256(request.SystemPrompt)}");
        Assert.DoesNotContain("House", request.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("roll-call vote of the United States Senate", request.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("checked against the Senate roll call data and Congress.gov data", request.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("states what the Senate voted on", request.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSystemPromptBansInternalWordsAndIsCached()
    {
        var world = VoteWorld.Create();
        var snapshot = (await world.Reader().ReadAsync(world.Evidence(), default)).Snapshot!;

        var request = VoteDraftPrompt.Build(snapshot, NoChoice(), VoteDraftTask.MaximumPromptBytes)!;

        Assert.True(request.CacheSystemPrompt);
        Assert.Contains("Never use the words tracked, entry, or candidate", request.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Never describe how Civic Lens works", request.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("tracked member", request.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARetryPromptCarriesTheRejectionReasonsAndTheSameFacts()
    {
        var world = VoteWorld.Create();
        var snapshot = (await world.Reader().ReadAsync(world.Evidence(), default)).Snapshot!;

        var first = VoteDraftPrompt.Build(snapshot, NoChoice(), 100_000)!;
        var retry = VoteDraftPrompt.Build(snapshot, NoChoice(), 100_000, ["headline is required."])!;

        Assert.Contains("- headline is required.", retry.UserPrompt, StringComparison.Ordinal);
        Assert.StartsWith(first.UserPrompt, retry.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAskedIssueChoiceListsEachCandidateAndAPolicyAreaWithNoIssueAsksForNone()
    {
        var world = VoteWorld.Create();
        var snapshot = (await world.Reader().ReadAsync(world.Evidence(), default)).Snapshot!;

        var asked = VoteDraftPrompt.Build(snapshot, BillGateFixtures.HousingChoice(), 100_000)!;
        var none = VoteDraftPrompt.Build(snapshot, NoChoice(), 100_000)!;

        Assert.Contains("\"issueIds\"", asked.SchemaJson, StringComparison.Ordinal);
        Assert.Contains("\"definition\": \"Rent costs.\"", asked.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("choose an issue only when the title of the measure is about it", asked.SystemPrompt, StringComparison.Ordinal);
        Assert.Equal(AnalysisSchema.HeadlineAndSummary(), none.SchemaJson);
        Assert.DoesNotContain("issueIds", none.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMinimumReservationIsTheSystemPromptPlusTheOutputAllowanceAndSettingsRejectALowerRunLimit()
    {
        Assert.True(VoteDraftPrompt.MinimumReservation > VoteDraftTask.MaximumOutputTokens);
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoteDraftSettings(VoteDraftPrompt.MinimumReservation - 1));
        Assert.Equal(VoteDraftPrompt.MinimumReservation - VoteDraftTask.MaximumOutputTokens,
            new VoteDraftSettings(VoteDraftPrompt.MinimumReservation).PromptByteCap);
    }

    [Fact]
    public async Task SubjectsReachTheModelAsFactsOnlyWhenTheBillHasThem()
    {
        var withSubjects = VoteWorld.Create(subjects: ["Housing finance and home ownership", "Rent and rental housing"]);
        var snapshot = (await withSubjects.Reader().ReadAsync(withSubjects.Evidence(), default)).Snapshot!;
        var plain = VoteWorld.Create();
        var plainSnapshot = (await plain.Reader().ReadAsync(plain.Evidence(), default)).Snapshot!;
        var choice = BillIssueChoice.For(BillGateFixtures.TrackedPolicyArea, snapshot.Subjects, new DateOnly(2025, 4, 10), BillGateFixtures.Issues());

        var asked = VoteDraftPrompt.Build(snapshot, choice, VoteDraftTask.MaximumPromptBytes)!;
        var plainRequest = VoteDraftPrompt.Build(plainSnapshot, BillGateFixtures.HousingChoice(), VoteDraftTask.MaximumPromptBytes)!;

        Assert.Contains("\"Rent and rental housing\"", asked.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("A subject does not make an issue fit.", asked.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("subjects", plainRequest.UserPrompt, StringComparison.Ordinal);
        Assert.Equal(SystemSha256, Sha256(plainRequest.SystemPrompt));
    }

    private static BillIssueChoice NoChoice() =>
        BillIssueChoice.For(BillGateFixtures.UntrackedPolicyArea, [], new DateOnly(2025, 4, 10), BillGateFixtures.Issues());

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
