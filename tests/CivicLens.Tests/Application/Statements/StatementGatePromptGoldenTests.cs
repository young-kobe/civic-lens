using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Statements;
using static CivicLens.Tests.Fixtures.StatementGateFixtures;

namespace CivicLens.Tests.Application.Statements;

public sealed class StatementGatePromptGoldenTests
{
    private const string SystemSha256 = "a95470b798416f4e860a604ca585a80cc77e1db7a78efb70e4d32ebb2cdc8ced";
    private const string SchemaSha256 = "70a578bf6bd993fd98f7a8de61e8daf123ddfdc900839526b92c2ab6af044661";
    private const string InputHash = "bf48c997830015be070d0e25330df9af271f5d42711aabb42a5cd1ec0b9be7a1";

    [Fact]
    public void ThePromptAndSchemaMatchTheirPinnedVersions()
    {
        var request = StatementGatePrompt.Build(Evidence("The senator announced a rent cap.\n", Observed, "https://example.test/news/release"),
            Rubric(), StatementGateTask.MaximumPromptBytes)!;
        var bump = $"The prompt or schema changed. Bump {nameof(StatementGateTask.PromptVersion)} " +
            $"({StatementGateTask.PromptVersion}) or {nameof(StatementGateTask.SchemaVersion)} " +
            $"({StatementGateTask.SchemaVersion}), and then update the pinned hashes in this test.";

        Assert.Equal("statement-gate-prompt-v2", StatementGateTask.PromptVersion);
        Assert.Equal("statement-gate-schema-v2", StatementGateTask.SchemaVersion);
        Assert.True(Sha256(request.SystemPrompt) == SystemSha256, $"{bump} System prompt: {Sha256(request.SystemPrompt)}");
        Assert.True(Sha256(request.SchemaJson) == SchemaSha256, $"{bump} Schema: {Sha256(request.SchemaJson)}");
        Assert.True(request.InputHash == InputHash, $"{bump} Input hash: {request.InputHash}");
    }

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
