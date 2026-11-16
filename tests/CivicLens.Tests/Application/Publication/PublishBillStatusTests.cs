using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishBillStatusTests
{
    private static readonly DateTimeOffset Observed = new(2026, 1, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AnActionWithAStoredStatusPinsItsRowButAnActionWithNoneGetsNoRowSoNoReaderSeesAFalseNoActionLine()
    {
        var scenario = new PublicationScenario();
        var withStatus = scenario.AddAction('c');
        var without = scenario.AddAction('d');
        scenario.Publications.BillStatuses[withStatus] = Status(withStatus, "Passed House.", new DateOnly(2026, 1, 28));

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", withStatus, without));

        var row = Assert.Single(Rows(scenario, summary));
        Assert.Equal(("Passed House.", new DateOnly(2026, 1, 28), Observed), (row.Text, row.Date, row.ObservedAtUtc));
        Assert.Equal(withStatus, row.RecordId);
    }

    [Fact]
    public async Task AReleaseHoldsNoStatusFileAndAPublishNeverReadsTheStatusFileOfAnOlderFolder()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        scenario.Publications.BillStatuses[action] = Status(action, "Passed House.", new DateOnly(2026, 1, 28));
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k1", action));
        scenario.Releases.Directories[first.DirectoryName][PublicationProtocol.BillStatusPath] = new byte[PublishedBillStatuses.MaximumBytes + 1];
        scenario.Releases.ReadPaths.Clear();

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", scenario.AddAction('d')));

        Assert.DoesNotContain(PublicationProtocol.BillStatusPath, scenario.Releases.Directories[second.DirectoryName].Keys);
        Assert.DoesNotContain(PublicationProtocol.BillStatusPath, scenario.Releases.ReadPaths);
        Assert.Equal(action, Assert.Single(Rows(scenario, second)).RecordId);
    }

    [Fact]
    public async Task AStoredStatusThatBreaksTheRowRulesFailsThePublishBeforeAnyStagingSoNoReleasePinsIt()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        scenario.Publications.BillStatuses[action] = Status(action, "Passed\u0007 House.", new DateOnly(2026, 1, 28));

        await Assert.ThrowsAsync<InvalidDataException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", action)));

        Assert.Equal(0, scenario.Releases.BeginCount);
        Assert.Empty(scenario.Publications.Committed);
    }

    [Fact]
    public async Task ACheckThatChangesNeitherTextNorDateKeepsThePinnedObservationTimeAndAMovedStatusReplacesIt()
    {
        var scenario = new PublicationScenario();
        var first = scenario.AddAction('c');
        var second = scenario.AddAction('d');
        scenario.Publications.BillStatuses[first] = Status(first, "Passed House.", new DateOnly(2026, 1, 28));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k1", first));

        scenario.Publications.BillStatuses[first] = Status(first, "Passed House.", new DateOnly(2026, 1, 28), Observed.AddDays(3));
        var checkedOnly = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", second));

        scenario.Publications.BillStatuses[first] = Status(first, "Became Public Law No: 119-1.", new DateOnly(2026, 2, 1));
        var moved = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k3"));

        Assert.Equal(Observed, Rows(scenario, checkedOnly).Single(row => row.RecordId == first).ObservedAtUtc);
        Assert.Equal(("Became Public Law No: 119-1.", new DateOnly(2026, 2, 1)),
            Rows(scenario, moved).Where(row => row.RecordId == first).Select(row => (row.Text, row.Date)).Single());
    }

    [Fact]
    public async Task APublishThatOnlyRefreshesAStatusStillPublishesButOneWithNothingChangedIsRefused()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k1", action));

        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k2")));
        scenario.Publications.BillStatuses[action] = Status(action, "Introduced in House", new DateOnly(2026, 1, 3));
        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k3"));

        Assert.Equal(action, Assert.Single(Rows(scenario, summary)).RecordId);
    }

    [Fact]
    public async Task APreviousReleaseWithNoPinnedRowReadsAsHavingNoStatusSoTheNextPublishPinsEveryRowThatNowExists()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        scenario.Publications.BillStatuses[action] = Status(action, "Passed House.", new DateOnly(2026, 1, 28));
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k1", action));
        scenario.Publications.PinnedBillStatuses[first.ReleaseNumber].Clear();

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k2"));

        var row = Assert.Single(Rows(scenario, summary));
        Assert.Equal((action, "Passed House."), (row.RecordId, row.Text));
    }

    [Fact]
    public async Task AWithdrawnActionAndACarriedEarlierWithdrawalHaveNoRowButAReturnedActionGetsItsRowAgain()
    {
        var scenario = new PublicationScenario();
        var first = scenario.AddAction('c');
        var second = scenario.AddAction('d');
        var kept = scenario.AddAction('f');
        foreach (var id in new[] { first, second, kept }) scenario.Publications.BillStatuses[id] = Status(id, "Passed House.", new DateOnly(2026, 1, 28));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k1", first, second, kept));

        var inThisPublish = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Withdraw("k2", first));
        var carried = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Withdraw("k3", second));
        scenario.ReviseAction(first, 2, correctionNote: "Back.");
        var returned = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k4", first));

        Assert.Equal(new[] { second, kept }.Order(StringComparer.Ordinal), Rows(scenario, inThisPublish).Select(row => row.RecordId));
        Assert.Equal([kept], Rows(scenario, carried).Select(row => row.RecordId));
        Assert.Equal(new[] { first, kept }.Order(StringComparer.Ordinal), Rows(scenario, returned).Select(row => row.RecordId));
    }

    [Fact]
    public async Task ARemovedStatusLeavesNoPinnedRow()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        scenario.Publications.BillStatuses[action] = Status(action, "Passed House.", new DateOnly(2026, 1, 28));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k1", action));
        scenario.Publications.BillStatuses.Clear();

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k2"));

        Assert.Empty(Rows(scenario, summary));
    }

    private static PublishReleaseRequest Withdraw(string key, string actionId) =>
        new([], key) { Withdrawals = [new(PublishedRecordKind.Action, actionId, "Wrong.")] };

    private static PublishReleaseRequest Request(string key, params string[] actionIds) =>
        new([.. actionIds.Select(id => new PublishedRecordReference(PublishedRecordKind.Action, id))], key);

    private static PublishedBillStatus Status(string recordId, string text, DateOnly date, DateTimeOffset? observed = null) =>
        new() { RecordId = recordId, Text = text, Date = date, ObservedAtUtc = observed ?? Observed };

    private static PublishedBillStatus[] Rows(PublicationScenario scenario, PublicationReleaseSummary release) =>
        [.. scenario.Publications.PinnedBillStatuses[release.ReleaseNumber].Values.OrderBy(row => row.RecordId, StringComparer.Ordinal)];
}
