using CivicLens.Application.Briefs;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Analysis;
using static CivicLens.Tests.Fixtures.BriefFixtures;

namespace CivicLens.Tests.Infrastructure.Briefs;

[Trait("Category", "LiveAi")]
public sealed class AnthropicBriefDraftLiveTests
{
    private const long TokenCap = 40_000;

    [LiveAiFact]
    public async Task OneWeekIsDraftedWithCitedParagraphsThatNameOnlyTheListedOfficials()
    {
        BriefRecord[] records =
        [
            Statement(new string('1', 64), "Warren backs a rent cap", 1, "warren"),
            DocumentChange(new string('2', 32), "A housing rule changed", 2),
            Action(new string('3', 64), "119 hr 1", 1, "markey")
        ];
        var context = Context(records);
        var request = BriefDraftPrompt.Build(context.Evidence.Window, context.Selection.Sent, Officials, BriefDraftTask.MaximumPromptBytes)!;
        Assert.InRange(request.ReservedTokens, 1, TokenCap);
        var model = new AnthropicBriefDraftModel(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")!);

        var first = await model.DraftAsync(request, CancellationToken.None);

        Assert.Equal("end_turn", first.StopReason);
        var resolution = BriefDraftResolver.Resolve(first.OutputJson!, context, new string('1', 32), DateTimeOffset.UnixEpoch);
        Assert.True(resolution.IsAccepted, string.Join("; ", resolution.Errors));
    }
}
