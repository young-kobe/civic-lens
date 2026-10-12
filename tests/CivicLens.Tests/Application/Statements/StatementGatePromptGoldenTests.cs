using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Statements;
using static CivicLens.Tests.Fixtures.StatementGateFixtures;

namespace CivicLens.Tests.Application.Statements;

public sealed class StatementGatePromptGoldenTests
{
    private const string SystemSha256 = "607dbb805c0c22499ca15754836747d6c22d633fbaed427fbed1b03e860eb857";
    private const string SchemaSha256 = "58d2bca2b13f65f6153a0e55014818cb39235eb784cef2b3b585ca18624acea9";
    private const string InputHash = "5845448690b9dfa6ce4735f36bc2f85f47c7e6870187d960ed286a4bc3e4fa1f";

    [Fact]
    public void ThePromptAndSchemaMatchTheirPinnedVersions()
    {
        var request = StatementGatePrompt.Build(Evidence("The senator announced a rent cap.\n", Observed, "https://example.test/news/release"),
            Rubric(), StatementGateTask.MaximumPromptBytes)!;
        var bump = $"The prompt or schema changed. Bump {nameof(StatementGateTask.PromptVersion)} " +
            $"({StatementGateTask.PromptVersion}) or {nameof(StatementGateTask.SchemaVersion)} " +
            $"({StatementGateTask.SchemaVersion}), and then update the pinned hashes in this test.";

        Assert.Equal("statement-gate-prompt-v1", StatementGateTask.PromptVersion);
        Assert.Equal("statement-gate-schema-v1", StatementGateTask.SchemaVersion);
        Assert.True(Sha256(request.SystemPrompt) == SystemSha256, $"{bump} System prompt: {Sha256(request.SystemPrompt)}");
        Assert.True(Sha256(request.SchemaJson) == SchemaSha256, $"{bump} Schema: {Sha256(request.SchemaJson)}");
        Assert.True(request.InputHash == InputHash, $"{bump} Input hash: {request.InputHash}");
    }

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
