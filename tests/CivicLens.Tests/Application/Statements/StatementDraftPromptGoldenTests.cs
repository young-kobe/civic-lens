using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Statements;
using CivicLens.Tests.Application.Analysis;
using static CivicLens.Tests.Fixtures.StatementDraftFixtures;

namespace CivicLens.Tests.Application.Statements;

public sealed class StatementDraftPromptGoldenTests
{
    private const string SingleSystemSha256 = "df8477e5ee278c64bd95b2f1ca155e43c91031692f35574397e4d362520c2db6";
    private const string SingleSchemaSha256 = "41ea98254bdbd68fd8709959111e16f9abc58c3b2193aa256ed23cc8221b6a9e";
    private const string SingleInputHash = "699b1d58cca880fcfdb24b0f5ffd6ca24ee680aa34c9cda6f164f708d831611a";
    private const string SeveralSystemSha256 = "213795c87d3790b22cc4095ca9539f4d32ab1bd018920f8bf4e492d7278fef1f";
    private const string SeveralSchemaSha256 = "898fc6633ce8ed825bb0a2a5dcab5ab8f84e1ead911bde296f1bfb9b5904a543";
    private const string SeveralInputHash = "242c0212b7a13517e79c9b1b371d28b40f4b9bef4584f835d5ea8d49ae51dced";

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

        Assert.Equal("statement-draft-prompt-v3", StatementDraftTask.PromptVersion);
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
