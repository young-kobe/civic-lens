using System.Text;
using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishBillStatusTests
{
    private static readonly DateTimeOffset Observed = new(2026, 1, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AnActionWithAStoredStatusPublishesItsRowAndRendersTheStatusButAnActionWithNoneGetsNeitherSoNoReaderSeesAFalseNoActionLine()
    {
        var scenario = new PublicationScenario();
        var withStatus = scenario.AddAction('c');
        var without = scenario.AddAction('d');
        scenario.Actions.BillStatuses[withStatus] = Status(withStatus, "Passed House.", new DateOnly(2026, 1, 28));

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", withStatus, without));

        var files = scenario.Releases.Directories[summary.DirectoryName];
        var row = Assert.Single(ReadStatuses(files).Records);
        Assert.Equal(("Passed House.", new DateOnly(2026, 1, 28), Observed), (row.Text, row.Date, row.ObservedAtUtc));
        Assert.Equal(withStatus, row.RecordId);
        Assert.Contains("status Passed House. 2026-01-28", Page(files, withStatus), StringComparison.Ordinal);
        Assert.DoesNotContain("status", Page(files, without), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACarriedActionPageIsWrittenAgainOnlyWhenTheLatestActionOrItsDateChangesSoACheckAloneRewritesNoPageAndTheFileKeepsTheTimeThePageShows()
    {
        var scenario = new PublicationScenario();
        var first = scenario.AddAction('c');
        var second = scenario.AddAction('d');
        scenario.Actions.BillStatuses[first] = Status(first, "Passed House.", new DateOnly(2026, 1, 28));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k1", first));
        scenario.Events.Clear();

        scenario.Actions.BillStatuses[first] = Status(first, "Passed House.", new DateOnly(2026, 1, 28), Observed.AddDays(3));
        var checkedOnly = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", second));
        var renderedAfterCheck = scenario.Events.Count(entry => entry == "render c");
        scenario.Events.Clear();

        scenario.Actions.BillStatuses[first] = Status(first, "Became Public Law No: 119-1.", new DateOnly(2026, 2, 1));
        var moved = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k3"));

        Assert.Equal(0, renderedAfterCheck);
        Assert.Equal(Observed, ReadStatuses(scenario.Releases.Directories[checkedOnly.DirectoryName]).Records.Single(row => row.RecordId == first).ObservedAtUtc);
        Assert.Equal(1, scenario.Events.Count(entry => entry == "render c"));
        Assert.Contains("status Became Public Law No: 119-1. 2026-02-01", Page(scenario.Releases.Directories[moved.DirectoryName], first), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APublishThatOnlyRefreshesAStatusStillPublishesButOneWithNothingChangedIsRefused()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k1", action));

        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k2")));
        scenario.Actions.BillStatuses[action] = Status(action, "Introduced in House", new DateOnly(2026, 1, 3));
        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k3"));

        Assert.Equal(action, Assert.Single(ReadStatuses(scenario.Releases.Directories[summary.DirectoryName]).Records).RecordId);
    }

    [Fact]
    public async Task AReleaseFromBeforeTheStatusFileReadsAsHavingNoStatusSoTheFirstPublishWritesEveryPageThatNowHasOne()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k1", action));
        var first = scenario.Publications.LastCommit!.DirectoryName;
        scenario.Releases.Directories[first].Remove(PublicationProtocol.BillStatusPath);
        scenario.Actions.BillStatuses[action] = Status(action, "Passed House.", new DateOnly(2026, 1, 28));

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k2"));

        Assert.Contains("status Passed House.", Page(scenario.Releases.Directories[summary.DirectoryName], action), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APreviousStatusFileOverTheByteCapIsRefusedOnReadSoNoPublishBuildsOnAnUnboundedFile()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k1", action));
        var first = scenario.Publications.LastCommit!.DirectoryName;
        scenario.Releases.Directories[first][PublicationProtocol.BillStatusPath] = new byte[PublishedBillStatuses.MaximumBytes + 1];

        await Assert.ThrowsAsync<InvalidDataException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", scenario.AddAction('d'))));
    }

    [Fact]
    public async Task AStatusRewriteKeepsTheRelatedPanelOfTheCarriedActionPageSoNoRelationshipLinkDisappears()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('b');
        var action = scenario.AddAction('d');
        scenario.Actions.BillStatuses[action] = Status(action, "Passed House.", new DateOnly(2026, 1, 28));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest(
            [new(PublishedRecordKind.Statement, statement), new(PublishedRecordKind.Action, action)], "k1"));
        var link = scenario.AddRelationship('e', statement, action);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([new(PublishedRecordKind.Relationship, link)], "k2"));
        scenario.Actions.BillStatuses[action] = Status(action, "Became Public Law No: 119-1.", new DateOnly(2026, 2, 1));

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k3"));

        var page = Page(scenario.Releases.Directories[summary.DirectoryName], action);
        Assert.Contains("status Became Public Law No: 119-1. 2026-02-01", page, StringComparison.Ordinal);
        Assert.Contains($" related {link[..1]}", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWithdrawnActionAndACarriedEarlierWithdrawalHaveNoRowButAReturnedActionGetsItsRowAgain()
    {
        var scenario = new PublicationScenario();
        var first = scenario.AddAction('c');
        var second = scenario.AddAction('d');
        var kept = scenario.AddAction('f');
        foreach (var id in new[] { first, second, kept }) scenario.Actions.BillStatuses[id] = Status(id, "Passed House.", new DateOnly(2026, 1, 28));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k1", first, second, kept));

        var inThisPublish = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Withdraw("k2", first));
        var carried = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Withdraw("k3", second));
        scenario.ReviseAction(first, 2, correctionNote: "Back.");
        var returned = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k4", first));

        Assert.Equal(new[] { second, kept }.Order(StringComparer.Ordinal), ReadStatuses(scenario.Releases.Directories[inThisPublish.DirectoryName]).Records.Select(row => row.RecordId));
        Assert.Equal([kept], ReadStatuses(scenario.Releases.Directories[carried.DirectoryName]).Records.Select(row => row.RecordId));
        Assert.Equal(new[] { first, kept }.Order(StringComparer.Ordinal), ReadStatuses(scenario.Releases.Directories[returned.DirectoryName]).Records.Select(row => row.RecordId));
        Assert.Contains("status Passed House.", Page(scenario.Releases.Directories[returned.DirectoryName], first), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARemovedStatusRowRendersTheCarriedPageAgainWithNoStatusLine()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        scenario.Actions.BillStatuses[action] = Status(action, "Passed House.", new DateOnly(2026, 1, 28));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k1", action));
        scenario.Actions.BillStatuses.Clear();

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k2"));

        Assert.DoesNotContain("status", Page(scenario.Releases.Directories[summary.DirectoryName], action), StringComparison.Ordinal);
        Assert.Empty(ReadStatuses(scenario.Releases.Directories[summary.DirectoryName]).Records);
    }

    private static PublishReleaseRequest Withdraw(string key, string actionId) =>
        new([], key) { Withdrawals = [new(PublishedRecordKind.Action, actionId, "Wrong.")] };

    private static PublishReleaseRequest Request(string key, params string[] actionIds) =>
        new([.. actionIds.Select(id => new PublishedRecordReference(PublishedRecordKind.Action, id))], key);

    private static PublishedBillStatus Status(string recordId, string text, DateOnly date, DateTimeOffset? observed = null) =>
        new() { RecordId = recordId, Text = text, Date = date, ObservedAtUtc = observed ?? Observed };

    private static PublishedBillStatuses ReadStatuses(IReadOnlyDictionary<string, byte[]> files) =>
        JsonSerializer.Deserialize<PublishedBillStatuses>(files[PublicationProtocol.BillStatusPath], PublicationProtocol.JsonOptions)!;

    private static string Page(IReadOnlyDictionary<string, byte[]> files, string recordId) =>
        Encoding.UTF8.GetString(files[PublicationProtocol.RecordPagePath(recordId)]);
}
