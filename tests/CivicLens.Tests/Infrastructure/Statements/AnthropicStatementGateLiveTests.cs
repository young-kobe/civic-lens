using CivicLens.Application.Statements;
using CivicLens.Infrastructure.Statements;
using CivicLens.Tests.Infrastructure.Analysis;
using static CivicLens.Tests.Fixtures.StatementGateFixtures;

namespace CivicLens.Tests.Infrastructure.Statements;

[Trait("Category", "LiveAi")]
public sealed class AnthropicStatementGateLiveTests
{
    private const long TokenCap = 25_000;

    [LiveAiFact]
    public async Task OneArticleIsGatedWithAVerifiedQuoteAndASecondRequestReadsTheCachedRubric()
    {
        var article = "Senator Example announced a bill to cap annual rent increases for tenants at three percent.\n";
        var evidence = Evidence(article);
        var rubric = Rubric();
        var request = StatementGatePrompt.Build(evidence, rubric, StatementGateTask.MaximumPromptBytes)!;
        Assert.InRange(request.ReservedTokens, 1, TokenCap);
        var model = new AnthropicStatementGateModel(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")!);

        var first = await model.ClassifyAsync(request, CancellationToken.None);
        var second = await model.ClassifyAsync(StatementGatePrompt.Build(Evidence(article + "A second article.\n"), rubric,
            StatementGateTask.MaximumPromptBytes)!, CancellationToken.None);

        Assert.Equal("end_turn", first.StopReason);
        var resolution = StatementGateResolver.Resolve(first.OutputJson!, evidence, rubric);
        Assert.True(resolution.IsValid, string.Join(" ", resolution.Errors));
        Assert.True(second.Usage.CacheReadTokens > 0, "The second request with the same rubric prefix must read the prompt cache.");
    }
}
