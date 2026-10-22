using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using Microsoft.Extensions.Logging;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublicationReleaseHandlersTests
{
    private static readonly DateTimeOffset Time = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ActivatingAnUnknownReleaseFailsBecauseOnlyCommittedReleasesMayGoLive()
    {
        var scenario = new PublicationScenario();
        var handler = scenario.Activator();

        await Assert.ThrowsAsync<ArgumentException>(() => handler.ExecuteAsync(PublicationScenario.Owner, 7));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => handler.ExecuteAsync(PublicationScenario.Owner, 0));
        Assert.Empty(scenario.Releases.Activated);
    }

    [Fact]
    public async Task OwnerCanRollBackToAnEarlierCommittedRelease()
    {
        var scenario = new PublicationScenario();
        await scenario.Publications.CommitAsync("owner", new(1, null, "release-1", [], []), "a", "h", default);
        await scenario.Publications.CommitAsync("owner", new(2, 1, "release-2", [], []), "b", "h", default);
        var handler = scenario.Activator();

        var summary = await handler.ExecuteAsync(PublicationScenario.Owner, 1);

        Assert.Equal("release-1", summary.Summary.DirectoryName);
        Assert.Equal("release-1", scenario.Releases.Active);
    }

    [Fact]
    public async Task ActivationServesTheRecordedActiveReleaseSoAnOlderRollbackCannotUndoANewerOne()
    {
        var scenario = new PublicationScenario();
        await scenario.Publications.CommitAsync("owner", new(1, null, "release-1", [], []), "a", "h", default);
        await scenario.Publications.CommitAsync("owner", new(2, 1, "release-2", [], []), "b", "h", default);
        scenario.Publications.BeforeServe = () => scenario.Publications.ActiveReleaseNumber = 2;

        await scenario.Activator().ExecuteAsync(PublicationScenario.Owner, 1);

        Assert.Equal("release-2", scenario.Releases.Active);
    }

    [Fact]
    public async Task ReviewerCannotActivateAReleaseButCanListThem()
    {
        var scenario = new PublicationScenario();
        await scenario.Publications.CommitAsync("owner", new(1, null, "release-1", [], []), "a", "h", default);
        await scenario.Releases.ActivateAsync("release-1", default);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            scenario.Activator().ExecuteAsync(PublicationScenario.Reviewer, 1));
        var list = await new ListPublicationReleases(scenario.Publications, scenario.Releases)
            .ExecuteAsync(PublicationScenario.Reviewer, 10);

        Assert.Single(list.Releases);
        Assert.Equal(1, list.ActiveReleaseNumber);
        Assert.Equal("release-1", list.ServedDirectoryName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task ListRejectsLimitsOutsideTheSharedPageBound(int limit)
    {
        var scenario = new PublicationScenario();
        var handler = new ListPublicationReleases(scenario.Publications, scenario.Releases);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => handler.ExecuteAsync(PublicationScenario.Reviewer, limit));
    }

    [Fact]
    public async Task RollingBackBeforeAWithdrawalReportsTheRestoredRecordBecauseReadersSeeItAgain()
    {
        var (scenario, action) = await PublishedActionAsync();
        await WithdrawAsync(scenario, action);

        var result = await scenario.Activator().ExecuteAsync(PublicationScenario.Owner, 1);

        Assert.Equal([action], result.RestoredWithdrawals);
        Assert.Empty(result.WithdrawnAgain);
        Assert.Equal(1, result.Summary.ReleaseNumber);
    }

    [Fact]
    public async Task ActivatingAReleaseThatWithdrewARecordReportsItWithdrawnAgain()
    {
        var (scenario, action) = await PublishedActionAsync();
        await WithdrawAsync(scenario, action);
        await scenario.Activator().ExecuteAsync(PublicationScenario.Owner, 1);

        var result = await scenario.Activator().ExecuteAsync(PublicationScenario.Owner, 2);

        Assert.Equal([action], result.WithdrawnAgain);
        Assert.Empty(result.RestoredWithdrawals);
    }

    [Fact]
    public async Task RollingBackToAReleaseWithARecordThatReviewNowBlocksReportsItBecauseReadersSeeItAgain()
    {
        var (scenario, action) = await PublishedActionAsync();
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k2", scenario.AddDraft('a')));
        scenario.Publications.ReviewStatuses[action] = ReviewStatus.ChangesRequested;

        var result = await scenario.Activator().ExecuteAsync(PublicationScenario.Owner, 1);

        var blocked = Assert.Single(result.Blocked);
        Assert.Equal((action, ReviewStatus.ChangesRequested), (blocked.Reference, blocked.ReviewStatus));
    }

    [Fact]
    public async Task ACleanRollbackReportsNothing()
    {
        var (scenario, _) = await PublishedActionAsync();
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k2", scenario.AddDraft('a')));

        var result = await scenario.Activator().ExecuteAsync(PublicationScenario.Owner, 1);

        Assert.Empty(result.RestoredWithdrawals);
        Assert.Empty(result.WithdrawnAgain);
        Assert.Empty(result.Blocked);
    }

    [Fact]
    public async Task AManifestThatCannotBeReadAfterTheSwitchStillReturnsTheSummaryAndLogsAWarning()
    {
        var (scenario, action) = await PublishedActionAsync();
        await WithdrawAsync(scenario, action);
        scenario.Releases.Directories[scenario.Publications.Committed[0].DirectoryName].Remove(PublicationProtocol.ManifestPath);
        var logger = new CapturingLogger<ActivatePublicationRelease>();

        var result = await scenario.Activator(logger).ExecuteAsync(PublicationScenario.Owner, 1);

        Assert.Equal(1, result.Summary.ReleaseNumber);
        Assert.Equal(scenario.Publications.Committed[0].DirectoryName, scenario.Releases.Active);
        Assert.Empty(result.RestoredWithdrawals);
        Assert.Empty(result.WithdrawnAgain);
        Assert.Empty(result.Blocked);
        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Entries).Level);
    }

    [Fact]
    public async Task AnUnreadableReplacedManifestEmptiesOnlyTheComparisonListsAndKeepsTheBlockedList()
    {
        var (scenario, action) = await PublishedActionAsync();
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k2", scenario.AddDraft('a')));
        scenario.Publications.ReviewStatuses[action] = ReviewStatus.ChangesRequested;
        scenario.Releases.Directories[scenario.Publications.Committed[1].DirectoryName].Remove(PublicationProtocol.ManifestPath);
        var logger = new CapturingLogger<ActivatePublicationRelease>();

        var result = await scenario.Activator(logger).ExecuteAsync(PublicationScenario.Owner, 1);

        Assert.Equal(action, Assert.Single(result.Blocked).Reference);
        Assert.Empty(result.RestoredWithdrawals);
        Assert.Empty(result.WithdrawnAgain);
        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Entries).Level);
    }

    [Fact]
    public async Task TheListsDescribeTheReleaseThatIsServedWhenAnotherActivationWonTheRace()
    {
        var (scenario, action) = await PublishedActionAsync();
        await WithdrawAsync(scenario, action);
        scenario.Publications.BeforeServe = () => scenario.Publications.ActiveReleaseNumber = 2;

        var result = await scenario.Activator().ExecuteAsync(PublicationScenario.Owner, 1);

        Assert.Equal(scenario.Publications.Committed[1].DirectoryName, scenario.Releases.Active);
        Assert.Empty(result.RestoredWithdrawals);
    }

    private static async Task<(PublicationScenario Scenario, PublishedRecordReference Action)> PublishedActionAsync()
    {
        var scenario = new PublicationScenario();
        var action = new PublishedRecordReference(PublishedRecordKind.Action, scenario.AddAction('c'));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", action));
        return (scenario, action);
    }

    private static Task WithdrawAsync(PublicationScenario scenario, PublishedRecordReference record) =>
        scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([], "k2") { Withdrawals = [new(record.Kind, record.RecordId, "Wrong.")] });
}
