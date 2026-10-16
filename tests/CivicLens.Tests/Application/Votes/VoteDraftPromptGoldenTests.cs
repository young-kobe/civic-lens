using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Votes;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Votes;

public sealed class VoteDraftPromptGoldenTests
{
    private const string SystemSha256 = "a9aad9119f18acb4fc26d9177b5cb515f2cba6ada70448821b45866e5b5ec793";
    private const string SchemaSha256 = "7e063b55e0c1bfc654088f5e9372850813294a033939acc1826b01b822aa7016";
    private const string InputHash = "4bfa1dc954e4cb4c237e95507e2cf4d8b8441d0c6b33bfafd7ba0fe29b31659e";

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
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoteDraftSettings(10_000_000, VoteDraftPrompt.MinimumReservation - 1));
        Assert.Equal(VoteDraftPrompt.MinimumReservation - VoteDraftTask.MaximumOutputTokens,
            new VoteDraftSettings(10_000_000, VoteDraftPrompt.MinimumReservation).PromptByteCap);
    }

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
