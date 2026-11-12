using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class BackfillReleaseBillCountsTests
{
    [Fact]
    public async Task ValidReleaseIsCommittedWithItsCheckedActionAndVoteMembersAndAnExactReplayIsAlreadyPresent()
    {
        var scenario = new PublicationScenario();
        var fixture = BillCountReleaseFixture.Add(scenario, 1);
        BillCountReleaseFixture.Add(scenario, 2);
        scenario.Publications.ExistingBillCounts.Add(2);

        var result = await Backfill(scenario).ExecuteAsync(PublicationScenario.Owner, 2, null);

        Assert.Equal((2, 1, 1, 0), (result.Examined, result.Added, result.AlreadyPresent, result.Failed));
        var committed = scenario.Publications.CommittedBillCounts.First(item => item.ReleaseNumber == 1);
        Assert.Equal(new[] { fixture.ActionBinding, fixture.VoteBinding }, committed.Members);
    }

    [Fact]
    public async Task LimitCountsReleasesAndTheReturnedCursorContinuesAfterTheLastExaminedRelease()
    {
        var scenario = new PublicationScenario();
        foreach (var number in new[] { 1, 2, 3 }) BillCountReleaseFixture.Add(scenario, number);
        var backfill = Backfill(scenario);

        var first = await backfill.ExecuteAsync(PublicationScenario.Owner, 2, null);
        var second = await backfill.ExecuteAsync(PublicationScenario.Owner, 2, ReleaseBillCountVerificationCursor.Parse(first.NextCursor));

        Assert.Equal(2, first.Examined);
        Assert.Equal(2, ReleaseBillCountVerificationCursor.Parse(first.NextCursor)!.ReleaseNumber);
        Assert.Equal(1, second.Examined);
        Assert.Null(second.NextCursor);
        Assert.Equal(new[] { 1, 2, 3 }, scenario.Publications.CommittedBillCounts.Select(item => item.ReleaseNumber));
    }

    [Fact]
    public async Task ChangedRetainedSelectionMakesAResumedBackfillCursorStale()
    {
        var scenario = new PublicationScenario();
        BillCountReleaseFixture.Add(scenario, 2);
        BillCountReleaseFixture.Add(scenario, 3);
        var backfill = Backfill(scenario);
        var first = await backfill.ExecuteAsync(PublicationScenario.Owner, 1, null);
        BillCountReleaseFixture.Add(scenario, 1);

        var resumed = await backfill.ExecuteAsync(PublicationScenario.Owner, 5, ReleaseBillCountVerificationCursor.Parse(first.NextCursor));

        Assert.True(resumed.StaleCursor);
        Assert.Null(resumed.NextCursor);
        Assert.DoesNotContain(scenario.Publications.CommittedBillCounts, item => item.ReleaseNumber == 1);
    }

    [Fact]
    public async Task SelectionThatChangesDuringFileReadsIsStaleBeforeTheBackfillIsDeclaredComplete()
    {
        var scenario = new PublicationScenario();
        BillCountReleaseFixture.Add(scenario, 1);
        scenario.Publications.BeforeReadingVerificationFingerprint = () => BillCountReleaseFixture.Add(scenario, 2);

        var result = await Backfill(scenario).ExecuteAsync(PublicationScenario.Owner, 5, null);

        Assert.True(result.StaleCursor);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task ReleaseExactlyAtTheLimitWithNothingAfterItHasNoCursor()
    {
        var scenario = new PublicationScenario();
        BillCountReleaseFixture.Add(scenario, 1);

        var result = await Backfill(scenario).ExecuteAsync(PublicationScenario.Owner, 1, null);

        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task ABrokenReleaseIsReportedAndNeverWrittenWhileLaterReleasesStillRun()
    {
        var scenario = new PublicationScenario();
        var broken = BillCountReleaseFixture.Add(scenario, 1);
        scenario.Releases.Directories[broken.Release.DirectoryName].Remove(PublicationProtocol.OfficialsPath);
        BillCountReleaseFixture.Add(scenario, 2);

        var result = await Backfill(scenario).ExecuteAsync(PublicationScenario.Owner, 2, null);

        Assert.Equal((2, 1, 1), (result.Examined, result.Added, result.Failed));
        Assert.Equal((1, "recordMissing"), (Assert.Single(result.Failures).ReleaseNumber, result.Failures[0].Reason));
        Assert.Equal(new[] { 2 }, scenario.Publications.CommittedBillCounts.Select(item => item.ReleaseNumber));
    }

    [Fact]
    public async Task FailureListIsBounded()
    {
        var scenario = new PublicationScenario();
        for (var number = 1; number <= 51; number++) scenario.Publications.BillCountReleases.Add(
            new(number, $"{number:D6}-{new string('a', 32)}", DateTimeOffset.UnixEpoch, 1));

        var result = await Backfill(scenario).ExecuteAsync(PublicationScenario.Owner, 51, null);

        Assert.Equal((51, 51, 50), (result.Examined, result.Failed, result.Failures.Count));
    }

    [Fact]
    public async Task RecordFileThatCannotBeParsedFailsTheReleaseBeforeAnyWrite()
    {
        var scenario = new PublicationScenario();
        var fixture = BillCountReleaseFixture.Add(scenario, 1);
        scenario.Releases.Directories[fixture.Release.DirectoryName][PublicationProtocol.RecordDataPath(fixture.Vote.RecordId)] = "not json"u8.ToArray();

        var result = await Backfill(scenario).ExecuteAsync(PublicationScenario.Owner, 1, null);

        Assert.Equal("recordInvalid", Assert.Single(result.Failures).Reason);
        Assert.Empty(scenario.Publications.CommittedBillCounts);
    }

    [Fact]
    public async Task MissingRecordFileFailsTheReleaseBeforeAnyWrite()
    {
        var scenario = new PublicationScenario();
        var fixture = BillCountReleaseFixture.Add(scenario, 1);
        scenario.Releases.Directories[fixture.Release.DirectoryName].Remove(PublicationProtocol.RecordDataPath(fixture.Action.RecordId));

        var result = await Backfill(scenario).ExecuteAsync(PublicationScenario.Owner, 1, null);

        Assert.Equal("recordMissing", Assert.Single(result.Failures).Reason);
        Assert.Empty(scenario.Publications.CommittedBillCounts);
    }

    [Fact]
    public async Task ActionFileWhoseCosponsorsDifferFromTheStoredRowsIsRefusedBecauseTheCountsComeFromTheRows()
    {
        var scenario = new PublicationScenario();
        var fixture = BillCountReleaseFixture.Add(scenario, 1);
        scenario.Publications.BillCountSources[1] = fixture.Source() with
        {
            Actions = [fixture.Source().Actions[0] with { CosponsorIds = ["o3"] }]
        };

        var result = await Backfill(scenario).ExecuteAsync(PublicationScenario.Owner, 1, null);

        Assert.Equal("recordInvalid", Assert.Single(result.Failures).Reason);
        Assert.Empty(scenario.Publications.CommittedBillCounts);
    }

    [Fact]
    public async Task WithdrawnCosponsorInTheFileIsNotACosponsorSoAStoredCosponsorIsRefused()
    {
        var scenario = new PublicationScenario();
        var fixture = BillCountReleaseFixture.Add(scenario, 1);
        var withdrawn = fixture.Action with
        {
            Sponsorships = [fixture.Action.Sponsorships[0], fixture.Action.Sponsorships[1] with { WithdrawnOn = BillCountReleaseFixture.EventDay }]
        };
        scenario.Releases.Directories[fixture.Release.DirectoryName] = fixture.Files(withdrawn);

        var result = await Backfill(scenario).ExecuteAsync(PublicationScenario.Owner, 1, null);

        Assert.Equal("recordInvalid", Assert.Single(result.Failures).Reason);
    }

    [Fact]
    public async Task ActionFileThatDiffersInCongressOrSponsorOrEventDateIsRefused()
    {
        var scenario = new PublicationScenario();
        var fixture = BillCountReleaseFixture.Add(scenario, 1);
        var stored = fixture.Source().Actions[0];
        foreach (var changed in new[]
        {
            stored with { Congress = 118 },
            stored with { SponsorIds = ["o9"] },
            stored with { EventOn = BillCountReleaseFixture.EventDay.AddDays(1) }
        })
        {
            scenario.Publications.BillCountSources[1] = fixture.Source() with { Actions = [changed] };

            var result = await Backfill(scenario).ExecuteAsync(PublicationScenario.Owner, 1, null);

            Assert.Equal("recordInvalid", Assert.Single(result.Failures).Reason);
        }
        Assert.Empty(scenario.Publications.CommittedBillCounts);
    }

    [Fact]
    public async Task VoteFileThatDiffersInCongressIsRefused()
    {
        var scenario = new PublicationScenario();
        var fixture = BillCountReleaseFixture.Add(scenario, 1);
        scenario.Publications.BillCountSources[1] = fixture.Source() with { Votes = [new(fixture.Vote.RecordId, fixture.Vote.RevisionNumber, 118)] };

        var result = await Backfill(scenario).ExecuteAsync(PublicationScenario.Owner, 1, null);

        Assert.Equal("recordInvalid", Assert.Single(result.Failures).Reason);
    }

    [Fact]
    public async Task ManifestAndTableMustListTheSameRevisionsSoMissingAndExtraMembersAreBothRefused()
    {
        var scenario = new PublicationScenario();
        var fixture = BillCountReleaseFixture.Add(scenario, 1);
        var source = fixture.Source();
        var extraRevision = fixture.ActionBinding with { RevisionNumber = 2 };
        var sources = new[]
        {
            source with { Members = [fixture.ActionBinding] },
            source with { Members = [.. source.Members, extraRevision] },
            source with { Members = [fixture.ActionBinding with { RevisionNumber = 2 }, fixture.VoteBinding] }
        };
        foreach (var changed in sources)
        {
            scenario.Publications.BillCountSources[1] = changed;

            var result = await Backfill(scenario).ExecuteAsync(PublicationScenario.Owner, 1, null);

            Assert.Equal("recordInvalid", Assert.Single(result.Failures).Reason);
        }
        Assert.Empty(scenario.Publications.CommittedBillCounts);
    }

    [Theory]
    [InlineData("extra")]
    [InlineData("missing")]
    public async Task OfficialsFileMustListExactlyTheStoredReleaseOfficials(string difference)
    {
        var scenario = new PublicationScenario();
        var fixture = BillCountReleaseFixture.Add(scenario, 1);
        scenario.Releases.Directories[fixture.Release.DirectoryName] = fixture.Files(officials: difference == "extra" ? ["o1", "o2", "o3"] : ["o1"]);

        var result = await Backfill(scenario).ExecuteAsync(PublicationScenario.Owner, 1, null);

        Assert.Equal("recordInvalid", Assert.Single(result.Failures).Reason);
        Assert.Empty(scenario.Publications.CommittedBillCounts);
    }

    [Fact]
    public async Task ReleaseRetiredOrRenamedBetweenTheFileReadAndTheWriteIsReportedAndOthersContinue()
    {
        var scenario = new PublicationScenario();
        BillCountReleaseFixture.Add(scenario, 1);
        BillCountReleaseFixture.Add(scenario, 2);
        scenario.Publications.BillCountCommitFailures[1] = new InvalidDataException("The prepared release is no longer retained.");

        var result = await Backfill(scenario).ExecuteAsync(PublicationScenario.Owner, 2, null);

        Assert.Equal((1, 1), (result.Added, result.Failed));
        Assert.Equal(("recordInvalid", 1), (result.Failures[0].Reason, result.Failures[0].ReleaseNumber));
    }

    [Fact]
    public async Task PartialExistingSnapshotIsReportedAsInvalidCountsAndNeverRepaired()
    {
        var scenario = new PublicationScenario();
        BillCountReleaseFixture.Add(scenario, 1);
        scenario.Publications.BillCountCommitFailures[1] = new BillCountRefusedException("Release 1 already has an incomplete or changed bill count snapshot.");

        var result = await Backfill(scenario).ExecuteAsync(PublicationScenario.Owner, 1, null);

        Assert.Equal((0, 1, "countsInvalid"), (result.Added, result.Failed, Assert.Single(result.Failures).Reason));
    }

    [Fact]
    public async Task OperationalErrorDuringCommitStopsTheRunInsteadOfBecomingAReleaseFailure()
    {
        var scenario = new PublicationScenario();
        BillCountReleaseFixture.Add(scenario, 1);
        scenario.Publications.BillCountCommitFailures[1] = new InvalidOperationException("The database connection closed.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Backfill(scenario).ExecuteAsync(PublicationScenario.Owner, 1, null));
    }

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException), "recordUnreadable")]
    [InlineData(typeof(IOException), "recordUnreadable")]
    [InlineData(typeof(DirectoryNotFoundException), "recordMissing")]
    [InlineData(typeof(JsonException), "recordInvalid")]
    public async Task UnreadableFilesAreReportedByKindOfFailure(Type failure, string reason)
    {
        var scenario = new PublicationScenario();
        var fixture = BillCountReleaseFixture.Add(scenario, 1);
        scenario.Releases.ReadFailures[PublicationProtocol.RecordDataPath(fixture.Vote.RecordId)] = (Exception)Activator.CreateInstance(failure, "Test failure")!;

        var result = await Backfill(scenario).ExecuteAsync(PublicationScenario.Owner, 1, null);

        Assert.Equal(reason, Assert.Single(result.Failures).Reason);
    }

    private static BackfillReleaseBillCounts Backfill(PublicationScenario scenario) => new(scenario.Publications, scenario.Releases);
}
