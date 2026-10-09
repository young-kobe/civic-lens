using CivicLens.Application.Publication;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublicationReleaseHandlersTests
{
    private static readonly DateTimeOffset Time = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ActivatingAnUnknownReleaseFailsBecauseOnlyCommittedReleasesMayGoLive()
    {
        var scenario = new PublicationScenario();
        var handler = new ActivatePublicationRelease(scenario.Publications, scenario.Releases);

        await Assert.ThrowsAsync<ArgumentException>(() => handler.ExecuteAsync(PublicationScenario.Owner, 7));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => handler.ExecuteAsync(PublicationScenario.Owner, 0));
        Assert.Empty(scenario.Releases.Activated);
    }

    [Fact]
    public async Task OwnerCanRollBackToAnEarlierCommittedRelease()
    {
        var scenario = new PublicationScenario();
        await scenario.Publications.CommitAsync("owner", new(1, "release-1", Time, [], []), "a", "h", default);
        await scenario.Publications.CommitAsync("owner", new(2, "release-2", Time, [], []), "b", "h", default);
        var handler = new ActivatePublicationRelease(scenario.Publications, scenario.Releases);

        var summary = await handler.ExecuteAsync(PublicationScenario.Owner, 1);

        Assert.Equal("release-1", summary.DirectoryName);
        Assert.Equal("release-1", scenario.Releases.Active);
    }

    [Fact]
    public async Task ReviewerCannotActivateAReleaseButCanListThem()
    {
        var scenario = new PublicationScenario();
        await scenario.Publications.CommitAsync("owner", new(1, "release-1", Time, [], []), "a", "h", default);
        await scenario.Releases.ActivateAsync("release-1", default);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new ActivatePublicationRelease(scenario.Publications, scenario.Releases).ExecuteAsync(PublicationScenario.Reviewer, 1));
        var list = await new ListPublicationReleases(scenario.Publications, scenario.Releases)
            .ExecuteAsync(PublicationScenario.Reviewer, 10);

        Assert.Single(list.Releases);
        Assert.Equal("release-1", list.ActiveDirectoryName);
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
}
