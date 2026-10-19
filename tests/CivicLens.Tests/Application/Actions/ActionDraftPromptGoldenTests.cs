using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.ActionBillWorld;

namespace CivicLens.Tests.Application.Actions;

public sealed class ActionDraftPromptGoldenTests
{
    private const string SystemSha256 = "71814fe66fb5dfd9b42380a66f64e6ae6892f81a327f823e6e8397627c2485e9";
    private const string SchemaSha256 = "7e063b55e0c1bfc654088f5e9372850813294a033939acc1826b01b822aa7016";
    private const string InputHash = "cafe29953361b38d4f577da1b626bed6c3be1857d6d98a61638462f9e8a88741";

    [Fact]
    public async Task ThePromptAndSchemaMatchTheirPinnedVersions()
    {
        var world = Create(cosponsorPages: [[new(Markey, "2025-01-04", Original: true)]],
            summaries: [new("00", "2025-01-03", "2025-01-05T10:00:00Z", "<p>It funds rent aid.</p>")]);
        var snapshot = (await world.Reader().ReadAsync(world.Evidence(), default)).Snapshot!;

        var request = ActionDraftPrompt.Build(snapshot, [new("rent", "Rent")], ActionDraftTask.MaximumPromptBytes)!;

        var bump = $"The prompt or schema changed. Bump {nameof(ActionDraftTask.PromptVersion)} ({ActionDraftTask.PromptVersion}) or " +
            $"{nameof(ActionDraftTask.SchemaVersion)} ({ActionDraftTask.SchemaVersion}), and then update the pinned hashes in this test.";
        Assert.Equal(("action-draft", "action-draft-v2", "action-draft-prompt-v2", "action-draft-schema-v1", "claude-haiku-5-5"),
            (ActionDraftTask.Task, ActionDraftTask.TaskVersion, ActionDraftTask.PromptVersion, ActionDraftTask.SchemaVersion, ActionDraftTask.Model));
        Assert.NotEqual(StatementDraftTaskName, ActionDraftTask.Task);
        Assert.True(Sha256(request.SystemPrompt) == SystemSha256, $"{bump} System prompt: {Sha256(request.SystemPrompt)}");
        Assert.True(Sha256(request.SchemaJson) == SchemaSha256, $"{bump} Schema: {Sha256(request.SchemaJson)}");
        Assert.True(request.InputHash == InputHash, $"{bump} Input hash: {request.InputHash}");
    }

    [Fact]
    public async Task ARetryPromptCarriesTheRejectionReasonsAndTheSameFacts()
    {
        var world = Create();
        var snapshot = (await world.Reader().ReadAsync(world.Evidence(), default)).Snapshot!;

        var first = ActionDraftPrompt.Build(snapshot, [], 100_000)!;
        var retry = ActionDraftPrompt.Build(snapshot, [], 100_000, ["headline is required."])!;

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

    private const string StatementDraftTaskName = "statement-draft";

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
