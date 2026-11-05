using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Actions;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Actions;

public sealed class ActionTextPromptGoldenTests
{
    private const string SystemSha256 = "510f744e27265dec57021c9da43fe913097b7c132b7af4cb596559e125f6ba93";
    private const string SchemaSha256 = "1842aea9479adba4bd6244a32833f17e655bb8083b18fb5c87380619258bc9e8";
    private const string InputHash = "b101751aa7e7c624d2fa244c6348ea032b21c844c22034965bc369d06e660b46";

    [Fact]
    public async Task TheTextPromptAndSchemaMatchTheirPinnedVersions()
    {
        var world = ActionBillWorld.Create(summaries: []);
        var snapshot = (await world.Reader().ReadAsync(world.Evidence() with { BillText = ActionBillTexts.Of() }, default)).Snapshot!;

        var request = ActionTextPrompt.Build(snapshot, ActionBillTexts.Of(), BillGateFixtures.HousingChoice(), ActionDraftTask.MaximumPromptBytes)!;

        var bump = $"The prompt or schema changed. Bump {nameof(ActionDraftTask.TextPromptVersion)} ({ActionDraftTask.TextPromptVersion}) or " +
            $"{nameof(ActionDraftTask.TextSchemaVersion)} ({ActionDraftTask.TextSchemaVersion}), and then update the pinned hashes in this test.";
        Assert.Equal(("action-text-prompt-v1", "action-text-schema-v1", "claude-haiku-5-5"),
            (ActionDraftTask.TextPromptVersion, ActionDraftTask.TextSchemaVersion, ActionDraftTask.Model));
        Assert.Equal((ActionDraftTask.TextPromptVersion, ActionDraftTask.TextSchemaVersion), (request.PromptVersion, request.SchemaVersion));
        Assert.Contains(ActionDraftTask.TextPromptVersion, ActionDraftTask.CurrentVersions.PromptVersions);
        Assert.Equal("action-draft-prompt-v5", ActionDraftTask.PromptVersion);
        Assert.True(request.CacheSystemPrompt);
        Assert.True(Sha256(request.SystemPrompt) == SystemSha256, $"{bump} System prompt: {Sha256(request.SystemPrompt)}");
        Assert.True(Sha256(request.SchemaJson) == SchemaSha256, $"{bump} Schema: {Sha256(request.SchemaJson)}");
        Assert.True(request.InputHash == InputHash, $"{bump} Input hash: {request.InputHash}");
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
