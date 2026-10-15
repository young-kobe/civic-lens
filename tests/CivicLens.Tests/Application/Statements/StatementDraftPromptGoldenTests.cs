using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Statements;
using static CivicLens.Tests.Fixtures.StatementDraftFixtures;

namespace CivicLens.Tests.Application.Statements;

public sealed class StatementDraftPromptGoldenTests
{
    private const string SingleSystemSha256 = "751b594553e4d748fb07f4afd6c5452f7705464aa7e82081f3cbea022ae14d2a";
    private const string SingleSchemaSha256 = "41ea98254bdbd68fd8709959111e16f9abc58c3b2193aa256ed23cc8221b6a9e";
    private const string SingleInputHash = "9d047fde562366e9df11afc29578e29badfba87de6b7a7f0c9face98175629e8";
    private const string SeveralSystemSha256 = "dec6b5032681a158ee96de5a2e65096100c25ae2aee517d0d176eea15503290c";
    private const string SeveralSchemaSha256 = "898fc6633ce8ed825bb0a2a5dcab5ab8f84e1ead911bde296f1bfb9b5904a543";
    private const string SeveralInputHash = "0c778ad41fba28bd463d4f31780a07999d05dd088f8a9e70cddaf3016d5b4b01";

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

        Assert.Equal("statement-draft-prompt-v1", StatementDraftTask.PromptVersion);
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
