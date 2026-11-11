using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class VerifyReleaseBillCountsTests
{
    [Fact]
    public async Task EveryRetainedReleaseIsCheckedAgainstItsFilesAndItsSavedRows()
    {
        var scenario = new PublicationScenario();
        BillCountReleaseFixture.Add(scenario, 1);
        BillCountReleaseFixture.Add(scenario, 2);

        var result = await Verify(scenario).ExecuteAsync(PublicationScenario.Owner, 5, null);

        Assert.Equal((2, 2, 0, false), (result.Examined, result.Verified, result.Failed, result.StaleCursor));
        Assert.Equal(new[] { 1, 2 }, scenario.Publications.VerifiedBillCounts);
    }

    [Theory]
    [InlineData(BillCountStatus.MissingRows, "countsMissing")]
    [InlineData(BillCountStatus.DifferentRows, "countsDifferent")]
    public async Task ReleaseWhoseSavedRowsAreMissingOrDifferentFailsAndOthersStillVerify(BillCountStatus status, string reason)
    {
        var scenario = new PublicationScenario();
        BillCountReleaseFixture.Add(scenario, 1);
        BillCountReleaseFixture.Add(scenario, 2);
        scenario.Publications.BillCountStatuses[1] = status;

        var result = await Verify(scenario).ExecuteAsync(PublicationScenario.Owner, 5, null);

        Assert.Equal((2, 1, 1), (result.Examined, result.Verified, result.Failed));
        Assert.Equal((1, reason), (result.Failures[0].ReleaseNumber, result.Failures[0].Reason));
    }

    [Fact]
    public async Task VerifierRepeatsTheFileChecksOfTheBackfillSoAChangedCosponsorFailsWithoutReadingSavedRows()
    {
        var scenario = new PublicationScenario();
        var fixture = BillCountReleaseFixture.Add(scenario, 1);
        scenario.Publications.BillCountSources[1] = fixture.Source() with { Actions = [fixture.Source().Actions[0] with { CosponsorIds = [] }] };

        var result = await Verify(scenario).ExecuteAsync(PublicationScenario.Owner, 5, null);

        Assert.Equal("recordInvalid", Assert.Single(result.Failures).Reason);
        Assert.Empty(scenario.Publications.VerifiedBillCounts);
    }

    [Fact]
    public async Task VerifierNeverWritesSavedRows()
    {
        var scenario = new PublicationScenario();
        BillCountReleaseFixture.Add(scenario, 1);

        await Verify(scenario).ExecuteAsync(PublicationScenario.Owner, 5, null);

        Assert.Empty(scenario.Publications.CommittedBillCounts);
    }

    [Fact]
    public async Task CursorContinuesAfterTheLastExaminedReleaseAndFinalPageHasNone()
    {
        var scenario = new PublicationScenario();
        foreach (var number in new[] { 1, 2, 3 }) BillCountReleaseFixture.Add(scenario, number);
        var verify = Verify(scenario);

        var first = await verify.ExecuteAsync(PublicationScenario.Owner, 2, null);
        var second = await verify.ExecuteAsync(PublicationScenario.Owner, 2, ReleaseBillCountVerificationCursor.Parse(first.NextCursor));

        Assert.NotNull(first.NextCursor);
        Assert.Equal(2, ReleaseBillCountVerificationCursor.Parse(first.NextCursor)!.ReleaseNumber);
        Assert.Equal((1, 1), (second.Examined, second.Verified));
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task CursorFromAnotherSelectionIsStaleAndNothingIsChecked()
    {
        var scenario = new PublicationScenario();
        BillCountReleaseFixture.Add(scenario, 1);
        var cursor = new ReleaseBillCountVerificationCursor(new string('0', 64), 1);

        var result = await Verify(scenario).ExecuteAsync(PublicationScenario.Owner, 5, cursor);

        Assert.True(result.StaleCursor);
        Assert.Equal(0, result.Examined);
        Assert.Empty(scenario.Publications.VerifiedBillCounts);
    }

    [Fact]
    public async Task SelectionThatChangesDuringFileReadsIsStaleBeforeTheScanIsDeclaredComplete()
    {
        var scenario = new PublicationScenario();
        BillCountReleaseFixture.Add(scenario, 1);
        scenario.Publications.BeforeReadingVerificationFingerprint = () => BillCountReleaseFixture.Add(scenario, 2);

        var result = await Verify(scenario).ExecuteAsync(PublicationScenario.Owner, 5, null);

        Assert.True(result.StaleCursor);
        Assert.Null(result.NextCursor);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc:1")]
    public void MalformedCursorIsRefused(string text) =>
        Assert.Throws<ArgumentException>(() => ReleaseBillCountVerificationCursor.Parse(text));

    [Fact]
    public void CursorWithALeadingZeroInTheReleaseNumberIsRefusedSoEveryCursorHasOneEncoding()
    {
        var fingerprint = new string('a', 64);

        Assert.Throws<ArgumentException>(() => ReleaseBillCountVerificationCursor.Parse($"{fingerprint}:07"));
        Assert.Equal(new ReleaseBillCountVerificationCursor(fingerprint, 7), ReleaseBillCountVerificationCursor.Parse($"{fingerprint}:7"));
    }

    private static VerifyReleaseBillCounts Verify(PublicationScenario scenario) => new(scenario.Publications, scenario.Releases);
}
