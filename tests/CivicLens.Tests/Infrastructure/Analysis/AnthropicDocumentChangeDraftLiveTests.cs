using CivicLens.Application.Analysis;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Infrastructure.Analysis;

[Trait("Category", "LiveAi")]
public sealed class AnthropicDocumentChangeDraftLiveTests
{
    private const long TokenCap = 25_000;

    [LiveAiFact]
    public async Task OneSmallComparisonDraftsWithValidCitationsWithinTheTokenCap()
    {
        var evidence = FakeDocumentChangeDraftModel.Evidence(
            "Applications open on October 1.\nThe deadline is October 15, 2026.\n",
            "Applications open on October 1.\nThe deadline is October 30, 2026.\n");
        var catalog = new DocumentChangeDraftCatalog([], []);
        var request = DocumentChangeDraftPrompt.Build(evidence, catalog)!;
        Assert.InRange(request.ReservedTokens, 1, TokenCap);
        var model = new AnthropicDocumentChangeDraftModel(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")!);

        var response = await model.DraftAsync(request, CancellationToken.None);

        Assert.Equal("end_turn", response.StopReason);
        Assert.InRange(response.Usage.Total, 1, request.ReservedTokens);
        var resolution = DocumentChangeDraftResolver.Resolve(response.OutputJson!, evidence, catalog, Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow);
        Assert.True(resolution.Revision is not null, string.Join(" ", resolution.Errors));
    }
}

internal sealed class LiveAiFactAttribute : FactAttribute
{
    public LiveAiFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CIVIC_LENS_LIVE_AI") != "1" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
            Skip = "Live Claude API test. Set CIVIC_LENS_LIVE_AI=1 and ANTHROPIC_API_KEY to run one paid call.";
    }
}
