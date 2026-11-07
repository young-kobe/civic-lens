using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishBillHistoryTests
{
    [Fact]
    public async Task ANewCompletedHistoryPublishesWithoutAStatusChangeAndDoesNotKeepTriggeringPublication()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        var first = Capture("first");
        scenario.Publications.BillHistories[action] = first;
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([new(PublishedRecordKind.Action, action)], "first-history"));
        Assert.Equal(new BillHistoryBinding(action, "first"), Assert.Single(scenario.Publications.LastCommit!.BillHistories));
        Assert.False(await scenario.Publisher().HasBillInputChangeAsync());

        scenario.Publications.BillHistories[action] = Capture("second");
        Assert.True(await scenario.Publisher().HasBillInputChangeAsync());
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "second-history"));
        Assert.Equal(new BillHistoryBinding(action, "second"), Assert.Single(scenario.Publications.LastCommit!.BillHistories));
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
            new PublishReleaseRequest([], "withdraw") { Withdrawals = [new(new(PublishedRecordKind.Action, action), "Removed for review.")] });
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
        Assert.Equal(new BillHistoryBinding(action, "selected"), Assert.Single(scenario.Publications.LastCommit!.BillHistories));
        Assert.True(await scenario.Publisher().HasBillInputChangeAsync());
    }

    private static BillHistoryCapture Capture(string id) => new(id, "congress", 119, "hr", 7,
        new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), 0);
}
