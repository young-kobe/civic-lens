using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Votes;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Votes;

public sealed class VoteDraftPromptGoldenTests
{
    private const string SystemSha256 = "ffc613b8ae00aa36ac1aa7e5269e73b7ba77d02174de5e2b9a5cebd0eaa408d3";
    private const string SenateSystemSha256 = "d07cca95001a4524cbe76297a8107a0d0a82c7669e9f1b2b4e7b1795462c942a";
    private const string SchemaSha256 = "7e063b55e0c1bfc654088f5e9372850813294a033939acc1826b01b822aa7016";
    private const string InputHash = "0bf5374daf51b7c4ddbb32dbc1fc866d6085281c49c59207733a6eb82e6b6c6f";

    [Fact]
    public async Task ThePromptAndSchemaMatchTheirPinnedVersions()
    {
        var world = VoteWorld.Create();
        var snapshot = (await world.Reader().ReadAsync(world.Evidence(), default)).Snapshot!;

        var request = VoteDraftPrompt.Build(snapshot, VoteDraftTask.MaximumPromptBytes)!;

        var bump = $"The prompt or schema changed. Bump {nameof(VoteDraftTask.PromptVersion)} ({VoteDraftTask.PromptVersion}) or " +
            $"{nameof(VoteDraftTask.SchemaVersion)} ({VoteDraftTask.SchemaVersion}), and then update the pinned hashes in this test.";
        Assert.Equal(("vote-draft", "vote-draft-v1", "vote-draft-prompt-v1", "vote-draft-schema-v1", "claude-haiku-5-5"),
            (VoteDraftTask.Task, VoteDraftTask.TaskVersion, VoteDraftTask.PromptVersion, VoteDraftTask.SchemaVersion, VoteDraftTask.Model));
        Assert.True(Sha256(request.SystemPrompt) == SystemSha256, $"{bump} System prompt: {Sha256(request.SystemPrompt)}");
        Assert.True(Sha256(request.SchemaJson) == SchemaSha256, $"{bump} Schema: {Sha256(request.SchemaJson)}");
        Assert.True(request.InputHash == InputHash, $"{bump} Input hash: {request.InputHash}");
    }

    [Fact]
    public async Task TheSenatePromptNamesTheSenateAndItsSourcesWhileTheHouseGoldenAbovePinsTheHouseBytes()
    {
        var world = SenateVoteWorld.Create(question: "On the Nomination", document: SenateVoteWorld.NominationDocument);
        var snapshot = (await world.Reader(SenateVoteWorld.Senators()).ReadAsync(world.Evidence(), default)).Snapshot!;

        var request = VoteDraftPrompt.Build(snapshot, VoteDraftTask.MaximumPromptBytes)!;

        var bump = $"The Senate prompt changed. Bump {nameof(VoteDraftTask.PromptVersion)} ({VoteDraftTask.PromptVersion}), and then update the pinned hash in this test.";
        Assert.True(Sha256(request.SystemPrompt) == SenateSystemSha256, $"{bump} System prompt: {Sha256(request.SystemPrompt)}");
        Assert.DoesNotContain("House", request.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("roll-call vote of the United States Senate", request.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("checked against the senate.gov and Congress.gov data", request.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("states what the Senate voted on", request.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARetryPromptCarriesTheRejectionReasonsAndTheSameFacts()
    {
        var world = VoteWorld.Create();
        var snapshot = (await world.Reader().ReadAsync(world.Evidence(), default)).Snapshot!;

        var first = VoteDraftPrompt.Build(snapshot, 100_000)!;
        var retry = VoteDraftPrompt.Build(snapshot, 100_000, ["headline is required."])!;

        Assert.Contains("- headline is required.", retry.UserPrompt, StringComparison.Ordinal);
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

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
