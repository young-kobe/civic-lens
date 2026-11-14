using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Relationships;
using CivicLens.Tests.Application.Analysis;
using static CivicLens.Tests.Fixtures.RelationshipFixtures;

namespace CivicLens.Tests.Application.Relationships;

public sealed class RelationshipDraftPromptGoldenTests
{
    private const string SystemSha256 = "57b4f5689e8b3f3d1523910917fcfda52b8a0e978b8fca00e8ec9c5aae84f763";
    private const string ActionSchemaSha256 = "dff9bafa14ef08eda3c44c0e88f2b9417df0becc8c1de733ff63d3761b5c9264";
    private const string VoteSchemaSha256 = "2ed54e5316072e9abca0da23011fabbcd3ea89fb52fd23b7f3d5cd2740c8701d";
    private const string InputHash = "2d8eadb6092348fb3113439336e548aad224dbc93d3bbe978dc960f3c91eb044";

    [Fact]
    public void ThePromptHoldsTheSharedAiTextSentencesButNoCodeGradeLimitBecauseAPersonApprovesTheReason() =>
        AiTextPromptRules.AssertHoldsSharedSentences(Build(ActionPair()).SystemPrompt, codeApproves: false, hasConfidence: false);

    [Fact]
    public void ThePromptAndSchemasMatchTheirPinnedVersions()
    {
        var action = Build(ActionPair());
        var vote = Build(VotePair());

        var bump = $"The prompt or schema changed. Bump {nameof(RelationshipDraftTask.PromptVersion)} ({RelationshipDraftTask.PromptVersion}) or " +
            $"{nameof(RelationshipDraftTask.SchemaVersion)} ({RelationshipDraftTask.SchemaVersion}), and then update the pinned hashes in this test.";
        Assert.Equal(("relationship-draft", "relationship-draft-v1", "relationship-draft-prompt-v2", "relationship-draft-schema-v1", "claude-haiku-5-5"),
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
        Assert.Throws<ArgumentOutOfRangeException>(() => new RelationshipDraftSettings(RelationshipDraftPrompt.MinimumReservation - 1));
        Assert.Equal(RelationshipDraftPrompt.MinimumReservation - RelationshipDraftTask.MaximumOutputTokens,
            new RelationshipDraftSettings(RelationshipDraftPrompt.MinimumReservation).PromptByteCap);
    }

    private static CivicLens.Application.Analysis.AnalysisModelRequest Build(CivicLens.Application.Relationships.RelationshipPair pair) =>
        RelationshipDraftPrompt.Build(Context(pair), Officials, RelationshipDraftTask.MaximumPromptBytes)!;

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
