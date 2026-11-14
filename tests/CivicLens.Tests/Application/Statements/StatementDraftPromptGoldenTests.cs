using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Statements;
using CivicLens.Tests.Application.Analysis;
using static CivicLens.Tests.Fixtures.StatementDraftFixtures;

namespace CivicLens.Tests.Application.Statements;

public sealed class StatementDraftPromptGoldenTests
{
    private const string SingleSystemSha256 = "3b50c78ff864bdbe287c2287bfd21756700a4d2f2edd858d61111aaa4a61d077";
    private const string SingleSchemaSha256 = "41ea98254bdbd68fd8709959111e16f9abc58c3b2193aa256ed23cc8221b6a9e";
    private const string SingleInputHash = "fdca07b618b5c52717a2301b29dd5ee35c8207f62f6f7fc84d4b055db8d1922b";
    private const string SeveralSystemSha256 = "78a933f7c0953ac6fd51167fb71c47f963b67b8bf22943ca9569d83cfc6928a9";
    private const string SeveralSchemaSha256 = "898fc6633ce8ed825bb0a2a5dcab5ab8f84e1ead911bde296f1bfb9b5904a543";
    private const string SeveralInputHash = "f6d3839aab041fb6a5b871a886f562ea19e9e57edb1bf1483ccea0ca40c1accd";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothPromptsHoldTheSharedAiTextSentencesAndStateTheCodeGradeLimit(bool severalSpeakers) =>
        AiTextPromptRules.AssertHoldsSharedSentences(StatementDraftPrompt.SystemPrompt(severalSpeakers), codeApproves: true);

    [Fact]
    public void TheSingleSpeakerPromptAndSchemaMatchTheirPinnedVersions() =>
        Pin(StatementDraftPrompt.Build(DraftEvidence("Senator Warren announced a rent cap.\n"), Officials(Warren), 100_000)!,
            SingleSystemSha256, SingleSchemaSha256, SingleInputHash);

    [Fact]
    public void TheSeveralSpeakersPromptAndSchemaMatchTheirPinnedVersions() =>
        Pin(StatementDraftPrompt.Build(DraftEvidence("Senator Warren announced a rent cap.\n"), Officials(Warren, Markey), 100_000)!,
            SeveralSystemSha256, SeveralSchemaSha256, SeveralInputHash);

    private static void Pin(CivicLens.Application.Analysis.AnalysisModelRequest request, string system, string schema, string input)
    {
        var bump = $"The prompt or schema changed. Bump {nameof(StatementDraftTask.PromptVersion)} " +
            $"({StatementDraftTask.PromptVersion}) or {nameof(StatementDraftTask.SchemaVersion)} " +
            $"({StatementDraftTask.SchemaVersion}), and then update the pinned hashes in this test.";

        Assert.Equal("statement-draft-prompt-v4", StatementDraftTask.PromptVersion);
        Assert.Equal("statement-draft-schema-v1", StatementDraftTask.SchemaVersion);
        Assert.Equal("statement-draft-v1", StatementDraftTask.TaskVersion);
        Assert.Equal("statement-draft", StatementDraftTask.Task);
        Assert.NotEqual(CivicLens.Application.Analysis.DocumentChangeDraftTask.Task, StatementDraftTask.Task);
        Assert.True(Sha256(request.SystemPrompt) == system, $"{bump} System prompt: {Sha256(request.SystemPrompt)}");
        Assert.True(Sha256(request.SchemaJson) == schema, $"{bump} Schema: {Sha256(request.SchemaJson)}");
        Assert.True(request.InputHash == input, $"{bump} Input hash: {request.InputHash}");
    }

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
