using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Relationships;
using static CivicLens.Tests.Fixtures.RelationshipFixtures;

namespace CivicLens.Tests.Application.Relationships;

public sealed class RelationshipDraftPromptGoldenTests
{
    private const string SystemSha256 = "7d84be6857fdd345d3ad36002b332b66bb202964dfd4458b2611ac7914e54192";
    private const string ActionSchemaSha256 = "dff9bafa14ef08eda3c44c0e88f2b9417df0becc8c1de733ff63d3761b5c9264";
    private const string VoteSchemaSha256 = "2ed54e5316072e9abca0da23011fabbcd3ea89fb52fd23b7f3d5cd2740c8701d";
    private const string InputHash = "6baa668b8813ef60f9f320cd1ee9987899375ac8bf867d94483f2d26a0a99b9c";

    [Fact]
    public void ThePromptAndSchemasMatchTheirPinnedVersions()
    {
        var action = Build(ActionPair());
        var vote = Build(VotePair());

        var bump = $"The prompt or schema changed. Bump {nameof(RelationshipDraftTask.PromptVersion)} ({RelationshipDraftTask.PromptVersion}) or " +
            $"{nameof(RelationshipDraftTask.SchemaVersion)} ({RelationshipDraftTask.SchemaVersion}), and then update the pinned hashes in this test.";
        Assert.Equal(("relationship-draft", "relationship-draft-v1", "relationship-draft-prompt-v1", "relationship-draft-schema-v1", "claude-haiku-5-5"),
            (RelationshipDraftTask.Task, RelationshipDraftTask.TaskVersion, RelationshipDraftTask.PromptVersion, RelationshipDraftTask.SchemaVersion,
                RelationshipDraftTask.Model));
        Assert.True(Sha256(action.SystemPrompt) == SystemSha256, $"{bump} System prompt: {Sha256(action.SystemPrompt)}");
        Assert.True(Sha256(action.SchemaJson) == ActionSchemaSha256, $"{bump} Action schema: {Sha256(action.SchemaJson)}");
        Assert.True(Sha256(vote.SchemaJson) == VoteSchemaSha256, $"{bump} Vote schema: {Sha256(vote.SchemaJson)}");
        Assert.True(action.InputHash == InputHash, $"{bump} Input hash: {action.InputHash}");
    }

    [Fact]
    public void TheSchemaOffersOnlyTheTypesOfTheTargetKindAndNoneSoTheModelCannotPickAWrongSide()
    {
        var action = Build(ActionPair()).SchemaJson;
        var vote = Build(VotePair()).SchemaJson;

        Assert.Contains("\"enum\":[\"describesOwnBill\",\"statesPositionOn\",\"claimsCreditFor\",\"none\"]", action, StringComparison.Ordinal);
        Assert.Contains("\"enum\":[\"statementAboutVote\",\"none\"]", vote, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSchemaOffersOnlyTheListedFactIds()
    {
        var schema = Build(ActionPair()).SchemaJson;

        Assert.Contains("\"enum\":[\"F1\",\"F2\",\"F3\",\"F4\"]", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("F5", schema, StringComparison.Ordinal);
    }

    [Fact]
    public void ARetryPromptCarriesTheRejectionReasonsAndTheSameEvidence()
    {
        var context = Context(ActionPair());

        var first = RelationshipDraftPrompt.Build(context, Officials, 100_000)!;
        var retry = RelationshipDraftPrompt.Build(context, Officials, 100_000, ["The quote is wrong."])!;

        Assert.Contains("- The quote is wrong.", retry.UserPrompt, StringComparison.Ordinal);
        Assert.StartsWith(first.UserPrompt, retry.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMinimumReservationIsTheSystemPromptPlusTheOutputAllowanceAndSettingsRejectALowerRunLimit()
    {
        Assert.True(RelationshipDraftPrompt.MinimumReservation > RelationshipDraftTask.MaximumOutputTokens);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RelationshipDraftSettings(10_000_000, RelationshipDraftPrompt.MinimumReservation - 1));
        Assert.Equal(RelationshipDraftPrompt.MinimumReservation - RelationshipDraftTask.MaximumOutputTokens,
            new RelationshipDraftSettings(10_000_000, RelationshipDraftPrompt.MinimumReservation).PromptByteCap);
    }

    private static CivicLens.Application.Analysis.AnalysisModelRequest Build(CivicLens.Application.Relationships.RelationshipPair pair) =>
        RelationshipDraftPrompt.Build(Context(pair), Officials, RelationshipDraftTask.MaximumPromptBytes)!;

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
