using CivicLens.Application.Actions;
using CivicLens.Infrastructure.Actions;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Analysis;
using static CivicLens.Tests.Fixtures.ActionBillWorld;

namespace CivicLens.Tests.Infrastructure.Actions;

[Trait("Category", "LiveAi")]
public sealed class AnthropicActionDraftingLiveTests
{
    private const long TokenCap = 40_000;

    [LiveAiFact]
    public async Task OneBillIsDraftedWithTextThatNamesOnlyTheListedOfficials()
    {
        var world = Create(cosponsorPages: [[new(Markey, "2025-01-04", Original: true)]],
            summaries: [new("00", "2025-01-03", "2025-01-05T10:00:00Z", "<p>It funds rent aid.</p>")]);
        var evidence = world.Evidence();
        var snapshot = (await world.Reader().ReadAsync(evidence, default)).Snapshot!;
        var request = ActionDraftPrompt.Build(snapshot, [new("rent", "Rent")], ActionDraftTask.MaximumPromptBytes)!;
        Assert.InRange(request.ReservedTokens, 1, TokenCap);
        var model = new AnthropicActionDraftingModel(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")!);

        var first = await model.DraftAsync(request, CancellationToken.None);

        Assert.Equal("end_turn", first.StopReason);
        var resolution = ActionDraftResolver.Resolve(first.OutputJson!, evidence, snapshot, ["rent"], "housing", BillGateFixtures.Revision().Id,
            new string('1', 32), DateTimeOffset.UnixEpoch);
        Assert.True(resolution.IsAccepted, string.Join("; ", resolution.Errors));
    }
}
