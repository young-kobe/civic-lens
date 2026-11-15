using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Issues;
using CivicLens.Application.Votes;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Votes;

public sealed class VoteDraftPromptGoldenTests
{
    private const string SystemSha256 = "58a0aa231547de0a324a7a9c8ba17d6910087dcbe2ab19ec4459462f07b4044a";
    private const string SchemaSha256 = "924ddf09fe3d1e1bc40739a6bbe685736955129192b0372e33a985c82a73855f";
    private const string InputHash = "3f62b6abae8f03c0240535e723c55d916b2b2670ce9ec3b986e93a4cb9c2be98";

    [Fact]
    public async Task TheIssueOnlyPromptAndSchemaMatchTheirPinnedVersions()
    {
        var world = VoteWorld.Create();
        var snapshot = (await world.Reader().ReadAsync(world.Evidence(), default)).Snapshot!;

        var request = VoteDraftPrompt.Build(snapshot, BillGateFixtures.HousingChoice(), VoteDraftTask.MaximumPromptBytes)!;

        var bump = $"The prompt or schema changed. Bump {nameof(VoteDraftTask.PromptVersion)} ({VoteDraftTask.PromptVersion}) or " +
            $"{nameof(VoteDraftTask.SchemaVersion)} ({VoteDraftTask.SchemaVersion}), and then update the pinned hashes in this test.";
        Assert.Equal(("vote-draft", "vote-draft-v1", "vote-draft-prompt-v8", "vote-draft-schema-v3", "claude-haiku-5-5"),
            (VoteDraftTask.Task, VoteDraftTask.TaskVersion, VoteDraftTask.PromptVersion, VoteDraftTask.SchemaVersion, VoteDraftTask.Model));
        Assert.True(Sha256(request.SystemPrompt) == SystemSha256, $"{bump} System prompt: {Sha256(request.SystemPrompt)}");
        Assert.True(Sha256(request.SchemaJson) == SchemaSha256, $"{bump} Schema: {Sha256(request.SchemaJson)}");
        Assert.True(request.InputHash == InputHash, $"{bump} Input hash: {request.InputHash}");
        Assert.True(request.CacheSystemPrompt);
    }

    [Fact]
    public async Task TheSenateGetsTheSameIssueOnlySystemPromptBecauseCodeNowWritesEveryWord()
    {
        var world = VoteviewVoteWorld.Create();
        var snapshot = (await world.Reader().ReadAsync(world.Evidence(), default)).Snapshot!;

        var request = VoteDraftPrompt.Build(snapshot, BillGateFixtures.HousingChoice(), VoteDraftTask.MaximumPromptBytes)!;

        Assert.Equal(SystemSha256, Sha256(request.SystemPrompt));
    }

    [Fact]
    public async Task AVoteWithNoCandidateIssueHasNoRequestBecauseTheStepNeverCallsTheModelForIt()
    {
        var world = VoteWorld.Create();
        var snapshot = (await world.Reader().ReadAsync(world.Evidence(), default)).Snapshot!;

        Assert.Throws<ArgumentException>(() => VoteDraftPrompt.Build(snapshot, NoChoice(), VoteDraftTask.MaximumPromptBytes));
    }

    [Fact]
    public async Task ARetryPromptCarriesTheRejectionReasonsAndTheSameFacts()
    {
        var world = VoteWorld.Create();
        var snapshot = (await world.Reader().ReadAsync(world.Evidence(), default)).Snapshot!;

        var first = VoteDraftPrompt.Build(snapshot, BillGateFixtures.HousingChoice(), 100_000)!;
        var retry = VoteDraftPrompt.Build(snapshot, BillGateFixtures.HousingChoice(), 100_000, ["confidence is required."])!;

        Assert.Contains("- confidence is required.", retry.UserPrompt, StringComparison.Ordinal);
        Assert.StartsWith(first.UserPrompt, retry.UserPrompt, StringComparison.Ordinal);
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
        Assert.Contains("\"definition\": \"Rent costs.\"", asked.UserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("subjects", plainRequest.UserPrompt, StringComparison.Ordinal);
    }

    private static BillIssueChoice NoChoice() =>
        BillIssueChoice.For(BillGateFixtures.UntrackedPolicyArea, [], new DateOnly(2025, 4, 10), BillGateFixtures.Issues());

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
