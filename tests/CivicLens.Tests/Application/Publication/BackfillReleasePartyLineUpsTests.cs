using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class BackfillReleasePartyLineUpsTests
{
    [Fact]
    public async Task EachReleaseIsCommittedOnceAndAnExactOrEmptySnapshotCountsAsAlreadyPresent()
    {
        var scenario = new PublicationScenario();
        BillCountReleaseFixture.Add(scenario, 1);
        BillCountReleaseFixture.Add(scenario, 2);
        scenario.Publications.ExistingPartyLineUps.Add(2);

        var result = await Backfill(scenario).ExecuteAsync(PublicationScenario.Owner, 2, 0);

        Assert.Equal((2, 1, 1, 0), (result.Examined, result.Added, result.AlreadyPresent, result.Failed));
        Assert.Equal(new[] { 1, 2 }, scenario.Publications.CommittedPartyLineUps);
        Assert.Empty(scenario.Publications.CommittedBillCounts);
    }

    [Fact]
    public async Task LimitCountsReleasesAndTheReturnedCursorContinuesAfterTheLastExaminedRelease()
    {
        var scenario = new PublicationScenario();
        foreach (var number in new[] { 1, 2, 3 }) BillCountReleaseFixture.Add(scenario, number);
        var backfill = Backfill(scenario);

        var first = await backfill.ExecuteAsync(PublicationScenario.Owner, 2, 0);
        var second = await backfill.ExecuteAsync(PublicationScenario.Owner, 2, first.NextCursor!.Value);

        Assert.Equal((2, 2, 1, null), (first.Examined, first.NextCursor, second.Examined, second.NextCursor));
        Assert.Equal(new[] { 1, 2, 3 }, scenario.Publications.CommittedPartyLineUps);
    }

    [Fact]
    public async Task ABrokenFileFailsTheReleaseBeforeAnyWriteAndAPartialSnapshotIsReportedAsLineUpsInvalid()
    {
        var scenario = new PublicationScenario();
        var broken = BillCountReleaseFixture.Add(scenario, 1);
        scenario.Releases.Directories[broken.Release.DirectoryName].Remove(PublicationProtocol.OfficialsPath);
        BillCountReleaseFixture.Add(scenario, 2);
        scenario.Publications.PartyLineUpCommitFailures[2] = new BillCountRefusedException("Partial line-up.");

        var result = await Backfill(scenario).ExecuteAsync(PublicationScenario.Owner, 2, 0);

        Assert.Equal((2, 0, 2), (result.Examined, result.Added, result.Failed));
        Assert.Equal(new[] { (1, "recordMissing"), (2, "lineUpsInvalid") }, result.Failures.Select(failure => (failure.ReleaseNumber, failure.Reason)));
        Assert.Empty(scenario.Publications.CommittedPartyLineUps);
    }

    [Fact]
    public async Task OnlyTheOwnerMayRunTheBackfillAndTheLimitMustBePositive()
    {
        var scenario = new PublicationScenario();
        var reviewer = new ReviewActor("auth0|reviewer", ReviewRole.Reviewer);

        await Assert.ThrowsAnyAsync<Exception>(() => Backfill(scenario).ExecuteAsync(reviewer, 1, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Backfill(scenario).ExecuteAsync(PublicationScenario.Owner, 0, 0));
    }

    private static BackfillReleasePartyLineUps Backfill(PublicationScenario scenario) => new(scenario.Publications, scenario.Releases);
}
