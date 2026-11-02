using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Briefs;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Briefs;

public sealed class BriefCheckPromptGoldenTests
{
    private const string SystemSha256 = "56636b4f8b707272530dc6452eb29acc67206eaf74c99dea80e36a762ead56d6";
    private const string SchemaSha256 = "3abb1e3791114e74a2ea35a1582228b8a935b2f64116985622c95f21bd80850b";
    private const string InputHash = "59b805ac6a45082787bef4a869606fd48747b78fe9961afdd2585fa02fdd94d3";

    [Fact]
    public void ThePromptAndSchemaMatchTheirPinnedVersions()
    {
        var request = Build();

        var bump = $"The prompt or schema changed. Bump {nameof(BriefCheckTask.PromptVersion)} ({BriefCheckTask.PromptVersion}) or " +
            $"{nameof(BriefCheckTask.SchemaVersion)} ({BriefCheckTask.SchemaVersion}), and then update the pinned hashes in this test.";
        Assert.Equal(("brief-check", "brief-check-v1", "brief-check-prompt-v1", "brief-check-schema-v1", "claude-sonnet-5-5"),
            (BriefCheckTask.Task, BriefCheckTask.TaskVersion, BriefCheckTask.PromptVersion, BriefCheckTask.SchemaVersion, BriefCheckTask.Model));
        Assert.True(Sha256(request.SystemPrompt) == SystemSha256, $"{bump} System prompt: {Sha256(request.SystemPrompt)}");
        Assert.True(Sha256(request.SchemaJson) == SchemaSha256, $"{bump} Schema: {Sha256(request.SchemaJson)}");
        Assert.True(request.InputHash == InputHash, $"{bump} Input hash: {request.InputHash}");
    }

    [Fact]
    public void TheSchemaClosesTheRecommendationTheCodesAndTheCitationIdsSoTheModelCannotInventOne()
    {
        var schema = Build().SchemaJson;

        Assert.Contains("\"enum\":[\"ApproveRecommended\",\"NeedsAttention\"]", schema, StringComparison.Ordinal);
        Assert.Contains("\"enum\":[\"unsupportedByCitation\",\"overstated\",\"statesBillStatus\",\"loadedWording\",\"omitsNotableRecord\",\"other\"]",
            schema, StringComparison.Ordinal);
        Assert.Contains("\"enum\":[\"R1\",\"R2\"]", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("onfidence", schema, StringComparison.Ordinal);
    }

    [Fact]
    public void ARetryPromptCarriesTheRejectionReasonsAndTheSameFacts()
    {
        var evidence = BriefCheckFixtures.Evidence();

        var first = BriefCheckPrompt.Build(evidence.Revision, evidence.Cited, evidence.NotCited, 100_000)!;
        var retry = BriefCheckPrompt.Build(evidence.Revision, evidence.Cited, evidence.NotCited, 100_000, ["flag 1 names paragraph 7."])!;

        Assert.Contains("- flag 1 names paragraph 7.", retry.UserPrompt, StringComparison.Ordinal);
        Assert.StartsWith(first.UserPrompt, retry.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void TheInputHashIgnoresTheRevisionNumberSoAnUnchangedTextIsNotCheckedTwice()
    {
        var evidence = BriefCheckFixtures.Evidence();
        var renumbered = evidence.Revision with { RevisionNumber = 9 };

        Assert.Equal(Build().InputHash,
            BriefCheckPrompt.Build(renumbered, evidence.Cited, evidence.NotCited, 100_000)!.InputHash);
    }

    [Fact]
    public void APromptOverTheByteCapIsNotBuilt()
    {
        var evidence = BriefCheckFixtures.Evidence();
        var size = Build().ReservedTokens - BriefCheckTask.MaximumOutputTokens;

        Assert.NotNull(BriefCheckPrompt.Build(evidence.Revision, evidence.Cited, evidence.NotCited, (int)size));
        Assert.Null(BriefCheckPrompt.Build(evidence.Revision, evidence.Cited, evidence.NotCited, (int)size - 1));
    }

    [Fact]
    public void TheMinimumReservationIsTheSystemPromptPlusTheOutputAllowanceAndSettingsRejectALowerRunLimit()
    {
        Assert.True(BriefCheckPrompt.MinimumReservation > BriefCheckTask.MaximumOutputTokens);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BriefCheckSettings(BriefCheckPrompt.MinimumReservation - 1));
    }

    private static CivicLens.Application.Analysis.AnalysisModelRequest Build()
    {
        var evidence = BriefCheckFixtures.Evidence();
        return BriefCheckPrompt.Build(evidence.Revision, evidence.Cited, evidence.NotCited, 100_000)!;
    }

    private static string Sha256(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
