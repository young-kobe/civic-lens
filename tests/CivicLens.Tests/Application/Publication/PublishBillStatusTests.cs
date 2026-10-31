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
    public async Task ACarriedActionPageIsWrittenAgainOnlyWhenTheLatestActionOrItsDateChangesSoACheckAloneRewritesNoPage()
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
        Assert.Equal(Observed.AddDays(3), ReadStatuses(scenario.Releases.Directories[checkedOnly.DirectoryName]).Records.Single(row => row.RecordId == first).ObservedAtUtc);
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

        await Assert.ThrowsAnyAsync<Exception>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", scenario.AddAction('d'))));
    }

    private static PublishReleaseRequest Request(string key, params string[] actionIds) =>
        new([.. actionIds.Select(id => new PublishedRecordReference(PublishedRecordKind.Action, id))], key);

    private static PublishedBillStatus Status(string recordId, string text, DateOnly date, DateTimeOffset? observed = null) =>
        new() { RecordId = recordId, Text = text, Date = date, ObservedAtUtc = observed ?? Observed };

    private static PublishedBillStatuses ReadStatuses(IReadOnlyDictionary<string, byte[]> files) =>
        JsonSerializer.Deserialize<PublishedBillStatuses>(files[PublicationProtocol.BillStatusPath], PublicationProtocol.JsonOptions)!;

    private static string Page(IReadOnlyDictionary<string, byte[]> files, string recordId) =>
        Encoding.UTF8.GetString(files[PublicationProtocol.RecordPagePath(recordId)]);
}
