using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Analysis;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Analysis;

public sealed class DocumentChangeDraftPromptGoldenTests
{
    private const string SystemSha256 = "005cca26866fb9dc12af1f2989486d9932c52c11f922ad8a12b97506587ebdf5";
    private const string SchemaSha256 = "8f8abe29b11bd7172d1a74aa9bfbd23c160226188292e81d157c09596539a261";
    private const string InputHash = "caf6ebebc59dbb3a88292cb63475c043e0e5bdeeb52e630e947d7e9dc542b479";

    [Fact]
    public void ThePromptAndSchemaMatchTheirPinnedVersions()
    {
        var request = DocumentChangeDraftPrompt.Build(FakeDocumentChangeDraftModel.Evidence("Deadline: October 15.\n",
            "Deadline: October 30.\n"), new DocumentChangeDraftCatalog([new("warren", "Elizabeth Warren")], ["housing"]))!;
        var bump = $"The prompt or schema changed. Bump {nameof(DocumentChangeDraftTask.PromptVersion)} " +
            $"({DocumentChangeDraftTask.PromptVersion}) or {nameof(DocumentChangeDraftTask.SchemaVersion)} " +
            $"({DocumentChangeDraftTask.SchemaVersion}), and then update the pinned hashes in this test.";

        Assert.Equal("document-change-draft-prompt-v3", DocumentChangeDraftTask.PromptVersion);
        Assert.Equal("document-change-draft-schema-v2", DocumentChangeDraftTask.SchemaVersion);
        Assert.True(Sha256(request.SystemPrompt) == SystemSha256, $"{bump} System prompt: {Sha256(request.SystemPrompt)}");
        Assert.True(Sha256(request.SchemaJson) == SchemaSha256, $"{bump} Schema: {Sha256(request.SchemaJson)}");
        Assert.True(request.InputHash == InputHash, $"{bump} Input hash: {request.InputHash}");
    }

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
