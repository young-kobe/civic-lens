using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Analysis;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Analysis;

public sealed class DocumentChangeDraftPromptGoldenTests
{
    private const string SystemSha256 = "2254036832f4a641320c5454f395fceaeaf82476f917aebd41307f0a3eaa971d";
    private const string SchemaSha256 = "8f8abe29b11bd7172d1a74aa9bfbd23c160226188292e81d157c09596539a261";
    private const string InputHash = "06c0d607b4111484ecec8c2d1b57809cc091c9a12eebe4ad8db023b90187c88a";

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

        Assert.Equal("document-change-draft-prompt-v3", DocumentChangeDraftTask.PromptVersion);
        Assert.Equal("document-change-draft-schema-v1", DocumentChangeDraftTask.SchemaVersion);
        Assert.True(Sha256(request.SystemPrompt) == SystemSha256, $"{bump} System prompt: {Sha256(request.SystemPrompt)}");
        Assert.True(Sha256(request.SchemaJson) == SchemaSha256, $"{bump} Schema: {Sha256(request.SchemaJson)}");
        Assert.True(request.InputHash == InputHash, $"{bump} Input hash: {request.InputHash}");
    }

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
