using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Statements;
using static CivicLens.Tests.Fixtures.StatementDraftFixtures;

namespace CivicLens.Tests.Application.Statements;

public sealed class StatementDraftPromptGoldenTests
{
    private const string SingleSystemSha256 = "77a90560e97fb4466621c84acd1245ff05be20abcb19f2d74021a2b2b71d0587";
    private const string SingleSchemaSha256 = "41ea98254bdbd68fd8709959111e16f9abc58c3b2193aa256ed23cc8221b6a9e";
    private const string SingleInputHash = "d6a6656d454c96d0dc1054ca6570583f038a71fa6b2b8b022a540940c12f2617";
    private const string SeveralSystemSha256 = "f8f9907f56a36cfdcefe3b0f85842a2264312fba5375427dd088146b5cc08053";
    private const string SeveralSchemaSha256 = "898fc6633ce8ed825bb0a2a5dcab5ab8f84e1ead911bde296f1bfb9b5904a543";
    private const string SeveralInputHash = "bd82fa5d1b44722d8fb1f080b0c0ef34f03cf6e54684927f0d9f0ec3d6875240";

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

        Assert.Equal("statement-draft-prompt-v2", StatementDraftTask.PromptVersion);
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
