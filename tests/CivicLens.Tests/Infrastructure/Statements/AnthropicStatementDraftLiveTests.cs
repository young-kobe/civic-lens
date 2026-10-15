using CivicLens.Application.Statements;
using CivicLens.Infrastructure.Statements;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Analysis;

namespace CivicLens.Tests.Infrastructure.Statements;

[Trait("Category", "LiveAi")]
public sealed class AnthropicStatementDraftLiveTests
{
    private const long TokenCap = 40_000;

    [LiveAiFact]
    public async Task OneArticleIsDraftedWithVerifiedQuotes()
    {
        var officials = StatementDraftFixtures.Officials(StatementDraftFixtures.Warren);
        var evidence = StatementDraftFixtures.DraftEvidence();
        var request = StatementDraftPrompt.Build(evidence, officials, StatementDraftTask.MaximumPromptBytes)!;
        Assert.InRange(request.ReservedTokens, 1, TokenCap);
        var model = new AnthropicStatementDraftModel(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")!);

        var first = await model.DraftAsync(request, CancellationToken.None);

        Assert.Equal("end_turn", first.StopReason);
        var resolution = StatementDraftResolver.Resolve(first.OutputJson!, evidence, officials, DateTimeOffset.UnixEpoch);
        Assert.NotNull(resolution.Revision);
    }
}
