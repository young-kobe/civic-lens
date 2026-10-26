using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.SenateVotes;
using CivicLens.Application.Collection.Votes;
using CivicLens.Collection.Contracts;
using CivicLens.Tests.Application.Collection.Bills;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.Jobs;

public sealed class CollectionJobPriorityTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 3, 1);

    [Fact]
    public void APageSourceJobIsACheckSoPressNeverWaitsForBackfills()
    {
        Assert.Equal(CollectionJobPriority.Check, CollectionFixtures.PageDefinition().Priority);
    }

    [Fact]
    public void ARosterJobIsACheck()
    {
        var roster = new ConfiguredCollectionSource(RosterFixtures.Configuration(), RosterFixtures.SourceId, Today);

        Assert.Equal(CollectionJobPriority.Check, roster.CreateScheduledJobDefinition(Now, null)!.Priority);
    }

    [Fact]
    public void TheLatestClosedBillWindowIsACheck()
    {
        var cursor = BillStreamWindow.LastClosedDay(Now).AddDays(-1);

        Assert.Equal(CollectionJobPriority.Check, BillSource().CreateScheduledJobDefinition(Now, cursor)!.Priority);
    }

    [Fact]
    public void AnOlderBillWindowIsABackfill()
    {
        Assert.Equal(CollectionJobPriority.Backfill, BillSource().CreateScheduledJobDefinition(Now, null)!.Priority);
    }

    [Fact]
    public void ABillDetailFollowsTheDayOfItsUpdate()
    {
        var window = new BillStreamWindow(Today.AddDays(-3), Today.AddDays(-1));
        var page = new CongressBillListPage([new CongressBillListItem(119, "hr", 1, Today.AddDays(-1)),
            new CongressBillListItem(119, "hr", 2, Today.AddDays(-30))], null, true, 0);

        var jobs = BillSource().CreateBillStreamAdmission("list", window, page, Today).Batch.Jobs;

        Assert.Equal([CollectionJobPriority.Check, CollectionJobPriority.Backfill],
            jobs.Select(job => job.Definition.Priority));
    }

    [Fact]
    public void AVoteListPageIsACheckInEverySessionSoCurrentVotesNeverWaitBehindABackfill()
    {
        var earlier = VoteSession.ForYear(2025);
        var house = HouseSource(Today);
        var senate = new ConfiguredCollectionSource(SenateVoteFixtures.Configuration(), SenateVoteFixtures.SourceId, Today);

        Assert.Equal(CollectionJobPriority.Check, house.CreateScheduledJobDefinition(Now, null)!.Priority);
        Assert.Equal(CollectionJobPriority.Check, senate.CreateScheduledJobDefinition(Now, null)!.Priority);
        Assert.Equal(CollectionJobPriority.Check,
            house.CreateHouseVoteListBatch("list", new HouseVoteListPage(earlier, [], null), Today).Jobs.Single().Definition.Priority);
        Assert.Equal(CollectionJobPriority.Check,
            senate.CreateSenateMenuBatch("menu", new SenateVoteMenu(earlier, []), Today).Jobs.Single().Definition.Priority);
    }

    [Fact]
    public void AVoteDetailAndMembersJobFollowTheYearOfTheirSession()
    {
        var earlier = new HouseVote(new HouseVoteRoll(VoteSession.ForYear(2025), 1), HouseVoteFixtures.Updated, null, null);
        var current = new HouseVote(new HouseVoteRoll(VoteSession.ForYear(2026), 1), HouseVoteFixtures.Updated, null, null);
        var house = HouseSource(Today);

        Assert.Equal(CollectionJobPriority.Backfill, house.CreateHouseVoteMembersJob(earlier, Today).Definition.Priority);
        Assert.Equal(CollectionJobPriority.Check, house.CreateHouseVoteMembersJob(current, Today).Definition.Priority);
        Assert.Equal([CollectionJobPriority.Backfill, CollectionJobPriority.Check],
            VoteDetailPriorities(house, new HouseVoteListPage(VoteSession.ForYear(2026),
                [new HouseVoteListItem(earlier.Roll, HouseVoteFixtures.Updated),
                    new HouseVoteListItem(current.Roll, HouseVoteFixtures.Updated)], null)));
    }

    [Fact]
    public void ABillFollowOnPageTakesThePriorityOfTheUpdateDayOfItsBill()
    {
        var recent = new CongressBillListItem(119, "hr", 1, Today.AddDays(-1));
        var old = new CongressBillListItem(119, "hr", 2, Today.AddDays(-30));

        Assert.Equal(CollectionJobPriority.Check, FirstPagePriority(recent));
        Assert.Equal(CollectionJobPriority.Backfill, FirstPagePriority(old));
        Assert.Equal(CollectionJobPriority.Backfill,
            BillSource().CreateNextPageJob(ApiResource.BillActions, "parent",
                "https://api.congress.gov/v3/bill/119/hr/2/actions?format=json&limit=250&offset=250", old.UpdateDate, Today).Definition.Priority);
    }

    [Fact]
    public void AReplayKeepsTheSavedPriorityEvenWhenTheRuleWouldNowChooseAnother()
    {
        var house = HouseSource(Today);
        var saved = house.CreateApiJobDefinition(ApiResource.HouseVoteDetail,
            "https://api.congress.gov/v3/house-vote/119/1/100?format=json", Today, CollectionJobPriority.Backfill);
        var wouldNowBe = house.CreateApiJobDefinition(ApiResource.HouseVoteDetail,
            saved.Url, Today, CollectionJobPriority.Check);

        var replay = house.CreateJobDefinition(Today, saved);

        Assert.NotEqual(wouldNowBe, saved);
        Assert.Equal(CollectionJobPriority.Backfill, replay.Priority);
    }

    [Fact]
    public void ADefinitionWithAnUnknownPriorityIsRejected()
    {
        var definition = CollectionFixtures.PageDefinition();

        definition.Validate();
        Assert.Throws<ArgumentException>(() => (definition with { Priority = 2 }).Validate());
    }

    private static int[] VoteDetailPriorities(ConfiguredCollectionSource source, HouseVoteListPage page) =>
        [.. source.CreateHouseVoteListBatch("list", page, Today).Jobs
            .Where(job => job.Definition.ApiResource == ApiResource.HouseVoteDetail)
            .Select(job => job.Definition.Priority)];

    private static int FirstPagePriority(CongressBillListItem bill) =>
        BillSource().CreateFirstPageJob(ApiResource.BillActions, bill,
            new DateTimeOffset(bill.UpdateDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), Today).Definition.Priority;

    private static ConfiguredCollectionSource BillSource() =>
        new(CongressBillSourceConfigurationTests.Configuration(), "congress-bills", Today);

    private static ConfiguredCollectionSource HouseSource(DateOnly asOf) =>
        new(HouseVoteFixtures.Configuration(), HouseVoteFixtures.SourceId, asOf);
}
