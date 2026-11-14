using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Analysis;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Analysis;

public sealed class DocumentChangeDraftPromptGoldenTests
{
    private const string SystemSha256 = "f5b649fb94f8180df47d14078c107b427e0e2004fc5ae5388c04da9df77d0f3b";
    private const string SchemaSha256 = "8f8abe29b11bd7172d1a74aa9bfbd23c160226188292e81d157c09596539a261";
    private const string InputHash = "369e95d7f87bac089dbe89f91c0c8a965c65a5cb8231b364f37997773258df1f";

    [Fact]
    public void ThePromptHoldsTheSharedAiTextSentencesButNoCodeGradeLimitBecauseAPersonApprovesTheAccount() =>
        AiTextPromptRules.AssertHoldsSharedSentences(DocumentChangeDraftPrompt.Build(FakeDocumentChangeDraftModel.Evidence("Deadline: October 15.\n",
            "Deadline: October 30.\n"), new DocumentChangeDraftCatalog([new("warren", "Elizabeth Warren")], ["housing"]))!.SystemPrompt, codeApproves: false);

    [Fact]
    public void ThePromptAndSchemaMatchTheirPinnedVersions()
    {
        var request = DocumentChangeDraftPrompt.Build(FakeDocumentChangeDraftModel.Evidence("Deadline: October 15.\n",
            "Deadline: October 30.\n"), new DocumentChangeDraftCatalog([new("warren", "Elizabeth Warren")], ["housing"]))!;
        var bump = $"The prompt or schema changed. Bump {nameof(DocumentChangeDraftTask.PromptVersion)} " +
            $"({DocumentChangeDraftTask.PromptVersion}) or {nameof(DocumentChangeDraftTask.SchemaVersion)} " +
            $"({DocumentChangeDraftTask.SchemaVersion}), and then update the pinned hashes in this test.";

        Assert.Equal("document-change-draft-prompt-v2", DocumentChangeDraftTask.PromptVersion);
        Assert.Equal("document-change-draft-schema-v1", DocumentChangeDraftTask.SchemaVersion);
        Assert.True(Sha256(request.SystemPrompt) == SystemSha256, $"{bump} System prompt: {Sha256(request.SystemPrompt)}");
        Assert.True(Sha256(request.SchemaJson) == SchemaSha256, $"{bump} Schema: {Sha256(request.SchemaJson)}");
        Assert.True(request.InputHash == InputHash, $"{bump} Input hash: {request.InputHash}");
    }

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
