using CivicLens.Application.Publication;

namespace CivicLens.Tests.Application.Publication;

public sealed class VerifyReleasePartyLineUpsTests
{
    [Theory]
    [InlineData(BillCountStatus.MissingRows, "lineUpsMissing")]
    [InlineData(BillCountStatus.DifferentRows, "lineUpsDifferent")]
    public async Task ReleaseWhoseSavedRowsAreMissingOrDifferentFailsAndOthersStillVerify(BillCountStatus status, string reason)
    {
        var scenario = new PublicationScenario();
        BillCountReleaseFixture.Add(scenario, 1);
        BillCountReleaseFixture.Add(scenario, 2);
        scenario.Publications.PartyLineUpStatuses[1] = status;

        var result = await Verify(scenario).ExecuteAsync(PublicationScenario.Owner, 5, null);

        Assert.Equal((2, 1, 1), (result.Examined, result.Verified, result.Failed));
        Assert.Equal((1, reason), (result.Failures[0].ReleaseNumber, result.Failures[0].Reason));
        Assert.Equal(new[] { 1, 2 }, scenario.Publications.VerifiedPartyLineUps);
        Assert.Empty(scenario.Publications.CommittedPartyLineUps);
    }

    [Fact]
    public async Task VerifierRepeatsTheFileChecksSoAChangedCosponsorFailsWithoutReadingSavedRows()
    {
        var scenario = new PublicationScenario();
        var fixture = BillCountReleaseFixture.Add(scenario, 1);
        scenario.Publications.BillCountSources[1] = fixture.Source() with { Actions = [fixture.Source().Actions[0] with { CosponsorIds = [] }] };

        var result = await Verify(scenario).ExecuteAsync(PublicationScenario.Owner, 5, null);

        Assert.Equal("recordInvalid", Assert.Single(result.Failures).Reason);
        Assert.Empty(scenario.Publications.VerifiedPartyLineUps);
    }

    [Fact]
    public async Task CursorContinuesAfterTheLastExaminedReleaseAndACursorFromAnotherSelectionIsStale()
    {
        var scenario = new PublicationScenario();
        BillCountReleaseFixture.Add(scenario, 1);
        BillCountReleaseFixture.Add(scenario, 2);
        var verify = Verify(scenario);

        var first = await verify.ExecuteAsync(PublicationScenario.Owner, 1, null);
        var second = await verify.ExecuteAsync(PublicationScenario.Owner, 1, ReleaseBillCountVerificationCursor.Parse(first.NextCursor));
        var stale = await verify.ExecuteAsync(PublicationScenario.Owner, 5, new ReleaseBillCountVerificationCursor(new string('0', 64), 1));

        Assert.Equal((1, 1), (first.Examined, second.Examined));
        Assert.Null(second.NextCursor);
        Assert.True(stale.StaleCursor);
        Assert.Equal(new[] { 1, 2 }, scenario.Publications.VerifiedPartyLineUps);
    }

    private static VerifyReleasePartyLineUps Verify(PublicationScenario scenario) => new(scenario.Publications, scenario.Releases);
}
