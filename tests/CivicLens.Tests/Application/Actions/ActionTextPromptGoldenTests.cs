using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Actions;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Actions;

public sealed class ActionTextPromptGoldenTests
{
    private const string SystemSha256 = "24ad3f4f1833a60613a2af9b467526f300635ef721f8c54e26e9eb168acf7372";
    private const string SchemaSha256 = "1842aea9479adba4bd6244a32833f17e655bb8083b18fb5c87380619258bc9e8";
    private const string InputHash = "57116b358ea1c1f64fbcac68872d92c4b3ca5ce194e73f6b89ce1c9057b9e605";

    [Fact]
    public async Task TheTextPromptAndSchemaMatchTheirPinnedVersions()
    {
        var world = ActionBillWorld.Create(summaries: []);
        var snapshot = (await world.Reader().ReadAsync(world.Evidence() with { BillText = ActionBillTexts.Of() }, default)).Snapshot!;

        var request = ActionTextPrompt.Build(snapshot, ActionBillTexts.Of(), BillGateFixtures.HousingChoice(), ActionDraftTask.MaximumPromptBytes)!;

        var bump = $"The prompt or schema changed. Bump {nameof(ActionDraftTask.TextPromptVersion)} ({ActionDraftTask.TextPromptVersion}) or " +
            $"{nameof(ActionDraftTask.TextSchemaVersion)} ({ActionDraftTask.TextSchemaVersion}), and then update the pinned hashes in this test.";
        Assert.Equal(("action-text-prompt-v6", "action-text-schema-v1", "claude-haiku-5-5"),
            (ActionDraftTask.TextPromptVersion, ActionDraftTask.TextSchemaVersion, ActionDraftTask.Model));
        Assert.Equal((ActionDraftTask.TextPromptVersion, ActionDraftTask.TextSchemaVersion), (request.PromptVersion, request.SchemaVersion));
        Assert.Contains(ActionDraftTask.TextPromptVersion, ActionDraftTask.CurrentVersions(draftsKeyTerms: false).PromptVersions);
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
        Assert.DoesNotContain("split the sentence", system, StringComparison.Ordinal);
        Assert.Contains("Keep the summary at 2 to 3 sentences.", system, StringComparison.Ordinal);
        Assert.Contains("After a long name is used once, use a short name for it, for example \"the Secretary\".", system, StringComparison.Ordinal);
        Assert.Contains("Name who the bill affects, what it changes, and each dollar amount in the bill text that is central to a change.", system, StringComparison.Ordinal);
        Assert.Contains("Name each date that the text sets for a change to start, end, or be due, for example a deadline or a fiscal year range, in the sentence that states the change.", system, StringComparison.Ordinal);
        Assert.Contains("helps, supports, addresses, or improves", system, StringComparison.Ordinal);
        Assert.Contains("reading grade 12 or below", system, StringComparison.Ordinal);
        Assert.Contains("Keep the exact term that the bill uses when the term carries the meaning, for example \"ceased operations\", \"loan guarantee\", or \"appropriations\". Explain the term in plain words in the same sentence or the next sentence.", system, StringComparison.Ordinal);
        Assert.Contains("Never turn \"shall be considered\" into \"is not treated as\". Never reverse a condition", system, StringComparison.Ordinal);
        Assert.DoesNotContain("A long name in a sentence is fine", system, StringComparison.Ordinal);
        Assert.DoesNotContain("short words", system, StringComparison.Ordinal);
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
