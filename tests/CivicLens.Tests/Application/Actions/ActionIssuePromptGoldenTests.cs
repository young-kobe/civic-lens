using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Actions;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.ActionBillWorld;

namespace CivicLens.Tests.Application.Actions;

public sealed class ActionIssuePromptGoldenTests
{
    private const string SystemSha256 = "c8bb0cd3ac262db5ac023cc98e4bac6c7550b7e20faad53463c69eeef6314e11";
    private const string SchemaSha256 = "924ddf09fe3d1e1bc40739a6bbe685736955129192b0372e33a985c82a73855f";
    private const string InputHash = "6f492c8b248e237039d3e83a21ffc75c88d9a9d00084a502f3b10f9091da589a";

    [Fact]
    public async Task TheIssueOnlyPromptAndSchemaMatchTheirPinnedVersions()
    {
        var world = Create(cosponsorPages: [[new(Markey, "2025-01-04", Original: true)]]);
        var snapshot = (await world.Reader().ReadAsync(world.Evidence(), default)).Snapshot!;

        var request = ActionIssuePrompt.Build(snapshot, BillGateFixtures.HousingChoice(), ActionDraftTask.MaximumPromptBytes)!;

        var bump = $"The prompt or schema changed. Bump {nameof(ActionDraftTask.IssuePromptVersion)} ({ActionDraftTask.IssuePromptVersion}) or " +
            $"{nameof(ActionDraftTask.IssueSchemaVersion)} ({ActionDraftTask.IssueSchemaVersion}), and then update the pinned hashes in this test.";
        Assert.Equal(("action-issue-prompt-v3", "action-issue-schema-v1", "claude-haiku-5-5"),
            (ActionDraftTask.IssuePromptVersion, ActionDraftTask.IssueSchemaVersion, ActionDraftTask.Model));
        Assert.Equal((ActionDraftTask.IssuePromptVersion, ActionDraftTask.IssueSchemaVersion), (request.PromptVersion, request.SchemaVersion));
        Assert.True(request.CacheSystemPrompt);
        Assert.True(Sha256(request.SystemPrompt) == SystemSha256, $"{bump} System prompt: {Sha256(request.SystemPrompt)}");
        Assert.True(Sha256(request.SchemaJson) == SchemaSha256, $"{bump} Schema: {Sha256(request.SchemaJson)}");
        Assert.True(request.InputHash == InputHash, $"{bump} Input hash: {request.InputHash}");
    }

    [Fact]
    public async Task TheIssueOnlyPromptSendsTheBillTheCandidatesAndTheSponsorsButNoSummaryAndAsksForIssuesAndAConfidenceOnly()
    {
        var world = Create(summaries: [new("00", "2025-01-03", "2025-01-05T10:00:00Z", "<p>It funds rent aid.</p>")]);
        var snapshot = (await world.Reader().ReadAsync(world.Evidence(), default)).Snapshot!;

        var request = ActionIssuePrompt.Build(snapshot, BillGateFixtures.HousingChoice(), ActionDraftTask.MaximumPromptBytes)!;

        Assert.DoesNotContain("rent aid", request.UserPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"summary\"", request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("\"definition\": \"Rent costs.\"", request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("Elizabeth Warren", request.UserPrompt, StringComparison.Ordinal);
        Assert.Equal("""{"type":"object","additionalProperties":false,"required":["issueIds","confidence"],"properties":{"issueIds":{"type":"array","items":{"type":"string","enum":["rent"]}},"confidence":{"type":"number"}}}""",
            request.SchemaJson);
    }

    [Fact]
    public async Task ARetryPromptCarriesTheRejectionReasonsAndTheIssueRequestStaysUnderTheByteCap()
    {
        var world = Create();
        var snapshot = (await world.Reader().ReadAsync(world.Evidence(), default)).Snapshot!;

        var first = ActionIssuePrompt.Build(snapshot, BillGateFixtures.HousingChoice(), ActionDraftTask.MaximumPromptBytes)!;
        var retry = ActionIssuePrompt.Build(snapshot, BillGateFixtures.HousingChoice(), ActionDraftTask.MaximumPromptBytes, ["issueIds is required."])!;

        Assert.Contains("- issueIds is required.", retry.UserPrompt, StringComparison.Ordinal);
        Assert.StartsWith(first.UserPrompt, retry.UserPrompt, StringComparison.Ordinal);
        Assert.Null(ActionIssuePrompt.Build(snapshot, BillGateFixtures.HousingChoice(), 100));
    }

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
