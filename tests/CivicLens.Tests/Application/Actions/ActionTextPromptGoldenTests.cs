using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Actions;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Actions;

public sealed class ActionTextPromptGoldenTests
{
    private const string SystemSha256 = "9cb731c8e8890c29c16ab1cc9fb5e51811cd4aa36ee70da8c581a694de40bdd8";
    private const string SchemaSha256 = "1842aea9479adba4bd6244a32833f17e655bb8083b18fb5c87380619258bc9e8";
    private const string InputHash = "a8dde3568365f042be7c99962d0e47e1dc1b51aa9bf145c3c9dd4a60b74b402d";

    [Fact]
    public async Task TheTextPromptAndSchemaMatchTheirPinnedVersions()
    {
        var world = ActionBillWorld.Create(summaries: []);
        var snapshot = (await world.Reader().ReadAsync(world.Evidence() with { BillText = ActionBillTexts.Of() }, default)).Snapshot!;

        var request = ActionTextPrompt.Build(snapshot, ActionBillTexts.Of(), BillGateFixtures.HousingChoice(), ActionDraftTask.MaximumPromptBytes)!;

        var bump = $"The prompt or schema changed. Bump {nameof(ActionDraftTask.TextPromptVersion)} ({ActionDraftTask.TextPromptVersion}) or " +
            $"{nameof(ActionDraftTask.TextSchemaVersion)} ({ActionDraftTask.TextSchemaVersion}), and then update the pinned hashes in this test.";
        Assert.Equal(("action-text-prompt-v4", "action-text-schema-v1", "claude-haiku-5-5"),
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
    public async Task TheSystemPromptAsksForSpecificSentencesAndSaysTheGradeLimitNeverAsksForFewerFacts()
    {
        var world = ActionBillWorld.Create(summaries: []);
        var snapshot = (await world.Reader().ReadAsync(world.Evidence() with { BillText = ActionBillTexts.Of() }, default)).Snapshot!;

        var system = ActionTextPrompt.Build(snapshot, ActionBillTexts.Of(), BillGateFixtures.HousingChoice(), ActionDraftTask.MaximumPromptBytes)!.SystemPrompt;

        Assert.Contains("It never asks for fewer facts.", system, StringComparison.Ordinal);
        Assert.Contains("If a fact needs a long word, split the sentence in two.", system, StringComparison.Ordinal);
        Assert.Contains("Name who the bill affects, what it changes, and each dollar amount, date, or deadline in the bill text that is central to a change.", system, StringComparison.Ordinal);
        Assert.Contains("helps, supports, addresses, or improves", system, StringComparison.Ordinal);
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
