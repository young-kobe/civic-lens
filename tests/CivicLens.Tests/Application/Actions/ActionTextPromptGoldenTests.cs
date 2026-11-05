using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Actions;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Actions;

public sealed class ActionTextPromptGoldenTests
{
    private const string SystemSha256 = "8aef78ca24072f663194e5866ec7699be587865df91690211b173a681d5dfc1d";
    private const string SchemaSha256 = "1842aea9479adba4bd6244a32833f17e655bb8083b18fb5c87380619258bc9e8";
    private const string InputHash = "32c99727c2757706ef78c8c483885878b8b7ba682450ca0582195a7f0da05e9f";

    [Fact]
    public async Task TheTextPromptAndSchemaMatchTheirPinnedVersions()
    {
        var world = ActionBillWorld.Create(summaries: []);
        var snapshot = (await world.Reader().ReadAsync(world.Evidence() with { BillText = ActionBillTexts.Of() }, default)).Snapshot!;

        var request = ActionTextPrompt.Build(snapshot, ActionBillTexts.Of(), BillGateFixtures.HousingChoice(), ActionDraftTask.MaximumPromptBytes)!;

        var bump = $"The prompt or schema changed. Bump {nameof(ActionDraftTask.TextPromptVersion)} ({ActionDraftTask.TextPromptVersion}) or " +
            $"{nameof(ActionDraftTask.TextSchemaVersion)} ({ActionDraftTask.TextSchemaVersion}), and then update the pinned hashes in this test.";
        Assert.Equal(("action-text-prompt-v2", "action-text-schema-v1", "claude-haiku-5-5"),
            (ActionDraftTask.TextPromptVersion, ActionDraftTask.TextSchemaVersion, ActionDraftTask.Model));
        Assert.Equal((ActionDraftTask.TextPromptVersion, ActionDraftTask.TextSchemaVersion), (request.PromptVersion, request.SchemaVersion));
        Assert.Contains(ActionDraftTask.TextPromptVersion, ActionDraftTask.CurrentPromptVersions);
        Assert.Equal("action-draft-prompt-v5", ActionDraftTask.PromptVersion);
        Assert.True(request.CacheSystemPrompt);
        Assert.True(Sha256(request.SystemPrompt) == SystemSha256, $"{bump} System prompt: {Sha256(request.SystemPrompt)}");
        Assert.True(Sha256(request.SchemaJson) == SchemaSha256, $"{bump} Schema: {Sha256(request.SchemaJson)}");
        Assert.True(request.InputHash == InputHash, $"{bump} Input hash: {request.InputHash}");
    }

    [Fact]
    public async Task TheTextCallReservesItsInputPlusTheLargerOutputLimitAndTheByteCapKeepsTheReservationInsideTheCallLimit()
    {
        var world = ActionBillWorld.Create(summaries: []);
        var snapshot = (await world.Reader().ReadAsync(world.Evidence() with { BillText = ActionBillTexts.Of() }, default)).Snapshot!;
        var request = ActionTextPrompt.Build(snapshot, ActionBillTexts.Of(), BillGateFixtures.HousingChoice(), ActionDraftTask.MaximumPromptBytes)!;
        var settings = new ActionDraftSettings(120_000);

        Assert.Equal(16_000, request.MaximumOutputTokens);
        Assert.Equal(Encoding.UTF8.GetByteCount(request.SystemPrompt) + Encoding.UTF8.GetByteCount(request.UserPrompt) +
            Encoding.UTF8.GetByteCount(request.SchemaJson) + 16_000, request.ReservedTokens);
        var small = new ActionDraftSettings(40_000);
        Assert.Equal(ActionDraftTask.MaximumPromptBytes, settings.TextPromptByteCap);
        Assert.Equal(small.CallTokenLimit - ActionDraftTask.MaximumTextOutputTokens, small.TextPromptByteCap);
        Assert.True(settings.TextPromptByteCap + ActionDraftTask.MaximumTextOutputTokens <= settings.CallTokenLimit);
        Assert.True(small.TextPromptByteCap + ActionDraftTask.MaximumTextOutputTokens <= small.CallTokenLimit);
    }

    [Fact]
    public async Task TheTextPromptSendsTheBillTextButNoSummaryAndARetryCarriesTheRejectionReasons()
    {
        var world = ActionBillWorld.Create(summaries: []);
        var text = ActionBillTexts.Of();
        var snapshot = (await world.Reader().ReadAsync(world.Evidence() with { BillText = text }, default)).Snapshot!;

        var first = ActionTextPrompt.Build(snapshot, text, BillGateFixtures.HousingChoice(), ActionDraftTask.MaximumPromptBytes)!;
        var retry = ActionTextPrompt.Build(snapshot, text, BillGateFixtures.HousingChoice(), ActionDraftTask.MaximumPromptBytes, ["The summary sentence is not an exact quote."])!;

        Assert.Contains(ActionBillTexts.FirstSummary, first.UserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"summary\"", first.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("- The summary sentence is not an exact quote.", retry.UserPrompt, StringComparison.Ordinal);
        Assert.StartsWith(first.UserPrompt, retry.UserPrompt, StringComparison.Ordinal);
        Assert.Null(ActionTextPrompt.Build(snapshot, text, BillGateFixtures.HousingChoice(), 100));
    }

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
