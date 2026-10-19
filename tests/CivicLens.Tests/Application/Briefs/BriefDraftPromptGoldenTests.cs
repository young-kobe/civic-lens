using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Briefs;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.BriefFixtures;

namespace CivicLens.Tests.Application.Briefs;

public sealed class BriefDraftPromptGoldenTests
{
    private const string SystemSha256 = "3f810a189375aee9ef7511c33237630cb8a245b178b818ad95f1dd6cc70ddbcc";
    private const string SchemaSha256 = "20e71ba28935b2ac806615f28e46f3fd239ef3b97f20b5ceb54f4658ae9176a0";
    private const string InputHash = "2c7f524e500c37f18a72380abba65136f5cd58991b0ae899ae2d0f97086c0b78";

    private static readonly BriefRecord[] Records =
    [
        Statement(new string('1', 64), "Warren on rent", 1, "warren"),
        DocumentChange(new string('2', 32), "A rule changed", 2)
    ];

    [Fact]
    public void ThePromptAndSchemaMatchTheirPinnedVersions()
    {
        var request = Build();

        var bump = $"The prompt or schema changed. Bump {nameof(BriefDraftTask.PromptVersion)} ({BriefDraftTask.PromptVersion}) or " +
            $"{nameof(BriefDraftTask.SchemaVersion)} ({BriefDraftTask.SchemaVersion}), and then update the pinned hashes in this test.";
        Assert.Equal(("brief-draft", "brief-draft-v1", "brief-draft-prompt-v1", "brief-draft-schema-v1", "claude-sonnet-5-5"),
            (BriefDraftTask.Task, BriefDraftTask.TaskVersion, BriefDraftTask.PromptVersion, BriefDraftTask.SchemaVersion, BriefDraftTask.Model));
        Assert.True(Sha256(request.SystemPrompt) == SystemSha256, $"{bump} System prompt: {Sha256(request.SystemPrompt)}");
        Assert.True(Sha256(request.SchemaJson) == SchemaSha256, $"{bump} Schema: {Sha256(request.SchemaJson)}");
        Assert.True(request.InputHash == InputHash, $"{bump} Input hash: {request.InputHash}");
    }

    [Fact]
    public void TheRequestAsksForTheBriefModelWithItsOutputAllowance()
    {
        var request = Build();

        Assert.Equal(("claude-sonnet-5-5", 8_000), (request.Model, request.MaximumOutputTokens));
        Assert.True(request.ReservedTokens > BriefDraftTask.MaximumOutputTokens);
    }

    [Fact]
    public void TheSchemaAllowsOnlyTheListedIdsSoTheModelCannotInventOne()
    {
        var schema = Build().SchemaJson;

        Assert.Contains("\"enum\":[\"R1\",\"R2\"]", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("R3", schema, StringComparison.Ordinal);
    }

    [Fact]
    public void ARetryPromptCarriesTheRejectionReasonsAndTheSameFacts()
    {
        var sent = SelectAll(Records).Sent;

        var first = BriefDraftPrompt.Build(Window(), sent, Officials, 100_000)!;
        var retry = BriefDraftPrompt.Build(Window(), sent, Officials, 100_000, ["headline is required."])!;

        Assert.Contains("- headline is required.", retry.UserPrompt, StringComparison.Ordinal);
        Assert.StartsWith(first.UserPrompt, retry.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void APromptOverTheByteCapIsNotBuilt()
    {
        var sent = SelectAll(Records).Sent;
        var size = BriefDraftPrompt.Build(Window(), sent, Officials, 100_000)!.ReservedTokens - BriefDraftTask.MaximumOutputTokens;

        Assert.NotNull(BriefDraftPrompt.Build(Window(), sent, Officials, (int)size));
        Assert.Null(BriefDraftPrompt.Build(Window(), sent, Officials, (int)size - 1));
    }

    [Fact]
    public void TheMinimumReservationIsTheSystemPromptPlusTheOutputAllowanceAndSettingsRejectALowerRunLimit()
    {
        Assert.True(BriefDraftPrompt.MinimumReservation > BriefDraftTask.MaximumOutputTokens);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BriefDraftSettings(BriefDraftPrompt.MinimumReservation - 1));
        Assert.Equal(BriefDraftPrompt.MinimumReservation - BriefDraftTask.MaximumOutputTokens,
            new BriefDraftSettings(BriefDraftPrompt.MinimumReservation).PromptByteCap);
    }

    private static CivicLens.Application.Analysis.AnalysisModelRequest Build() =>
        BriefDraftPrompt.Build(Window(), SelectAll(Records).Sent, Officials, BriefDraftTask.MaximumPromptBytes)!;

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
