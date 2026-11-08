using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishBillHistoryTests
{
    [Fact]
    public async Task AVerifiedRepresentationOfTheSameCapturePublishesOnceWithoutChangingItsObservationTime()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        var legacy = Capture("same-capture") with { ProjectionVersion = 0 };
        scenario.Publications.BillHistories[action] = legacy;
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([new(PublishedRecordKind.Action, action)], "legacy-history"));
        var prior = scenario.Publications.LastCommit!;

        scenario.Publications.BillHistories[action] = legacy with { ProjectionVersion = BillHistoryCapture.CurrentProjectionVersion };
        Assert.True(await scenario.Publisher().HasBillInputChangeAsync());
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "verified-history"));

        Assert.Equal(0, Assert.Single(prior.BillHistories).ProjectionVersion);
        Assert.Equal(new BillHistoryBinding(action, legacy.DetailJobId, BillHistoryCapture.CurrentProjectionVersion),
            Assert.Single(scenario.Publications.LastCommit!.BillHistories));
        Assert.Equal(prior.ReleaseNumber + 1, scenario.Publications.LastCommit.ReleaseNumber);
        Assert.Equal(legacy.ObservedAtUtc, scenario.Publications.PreparedHistories.Last().ObservedAtUtc);
        Assert.False(await scenario.Publisher().HasBillInputChangeAsync());
    }

    [Fact]
    public async Task UnverifiedLegacyHistoryDoesNotBlockCarriedBillsOrKeepAnEmptyHistoryClaim()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        scenario.Publications.BillHistories[action] = Capture("legacy") with { ProjectionVersion = 0 };
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([new(PublishedRecordKind.Action, action)], "legacy-history"));
        var prior = scenario.Publications.LastCommit!;
        scenario.Publications.BillHistories.Remove(action);

        Assert.True(await scenario.Publisher().HasBillInputChangeAsync());
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "no-verified-history"));

        Assert.Single(prior.BillHistories);
        Assert.Empty(scenario.Publications.LastCommit!.BillHistories);
        Assert.Equal(action, Assert.Single(scenario.Publications.LastCommit.Records).RecordId);
        Assert.False(await scenario.Publisher().HasBillInputChangeAsync());
    }

    [Fact]
    public async Task ANewCompletedHistoryPublishesWithoutAStatusChangeAndDoesNotKeepTriggeringPublication()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        var first = Capture("first");
        scenario.Publications.BillHistories[action] = first;
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([new(PublishedRecordKind.Action, action)], "first-history"));
        Assert.Equal(new BillHistoryBinding(action, "first", BillHistoryCapture.CurrentProjectionVersion), Assert.Single(scenario.Publications.LastCommit!.BillHistories));
        Assert.False(await scenario.Publisher().HasBillInputChangeAsync());

        scenario.Publications.BillHistories[action] = Capture("second");
        Assert.True(await scenario.Publisher().HasBillInputChangeAsync());
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "second-history"));
        Assert.Equal(new BillHistoryBinding(action, "second", BillHistoryCapture.CurrentProjectionVersion), Assert.Single(scenario.Publications.LastCommit!.BillHistories));
        Assert.False(await scenario.Publisher().HasBillInputChangeAsync());
        Assert.Equal([first, Capture("second")], scenario.Publications.PreparedHistories);
    }

    [Fact]
    public async Task WithdrawingABillPreparesAndBindsNoHistoryForTheRemovedRecord()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        scenario.Publications.BillHistories[action] = Capture("first");
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([new(PublishedRecordKind.Action, action)], "publish"));
        scenario.Publications.PreparedHistories.Clear();
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([], "withdraw") { Withdrawals = [new(PublishedRecordKind.Action, action, "Removed for review.")] });
        Assert.Empty(scenario.Publications.LastCommit!.BillHistories);
        Assert.Empty(scenario.Publications.PreparedHistories);
    }

    [Fact]
    public async Task AHistoryCompletedAfterSelectionWaitsForTheNextRelease()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        var selected = Capture("selected");
        scenario.Publications.BillHistories[action] = selected;
        scenario.Publications.BeforePreparingHistories = () => scenario.Publications.BillHistories[action] = Capture("later");

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([new(PublishedRecordKind.Action, action)], "selected-history"));

        Assert.Equal([selected], scenario.Publications.PreparedHistories);
        Assert.Equal(new BillHistoryBinding(action, "selected", BillHistoryCapture.CurrentProjectionVersion), Assert.Single(scenario.Publications.LastCommit!.BillHistories));
        Assert.True(await scenario.Publisher().HasBillInputChangeAsync());
    }

    private static BillHistoryCapture Capture(string id) => new(id, "congress", 119, "hr", 7,
        new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), 0, BillHistoryCapture.CurrentProjectionVersion);
}
