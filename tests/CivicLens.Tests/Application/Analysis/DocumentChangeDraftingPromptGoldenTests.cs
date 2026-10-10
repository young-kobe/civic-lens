using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Analysis;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Analysis;

public sealed class DocumentChangeDraftingPromptGoldenTests
{
    private const string SystemSha256 = "ce00e67006855e08d2c6ed64c19529f1a5773d4c7dfd19f06e28c4da7746cecf";
    private const string SchemaSha256 = "a41cd47eb599bb95d23db928212331891a31c9c3d84abc6a25e9077e778a8e3a";
    private const string InputHash = "5ee6a43d7aec52ba1579f72632e3a4bf68663e83f4f25c9395000951027bca84";

    [Fact]
    public void ThePromptAndSchemaMatchTheirPinnedVersions()
    {
        var request = DocumentChangeDraftingPrompt.Build(FakeDocumentChangeDraftingModel.Evidence("Deadline: October 15.\n",
            "Deadline: October 30.\n"), new DocumentChangeAnalysisCatalog([new("warren", "Elizabeth Warren")], ["housing"]))!;
        var bump = $"The prompt or schema changed. Bump {nameof(DocumentChangeDraftingTask.PromptVersion)} " +
            $"({DocumentChangeDraftingTask.PromptVersion}) or {nameof(DocumentChangeDraftingTask.SchemaVersion)} " +
            $"({DocumentChangeDraftingTask.SchemaVersion}), and then update the pinned hashes in this test.";

        Assert.True(Sha256(request.SystemPrompt) == SystemSha256, $"{bump} System prompt: {Sha256(request.SystemPrompt)}");
        Assert.True(Sha256(request.SchemaJson) == SchemaSha256, $"{bump} Schema: {Sha256(request.SchemaJson)}");
        Assert.True(request.InputHash == InputHash, $"{bump} Input hash: {request.InputHash}");
    }

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
