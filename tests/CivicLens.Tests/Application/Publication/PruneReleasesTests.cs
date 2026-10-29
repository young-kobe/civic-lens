using CivicLens.Application.Publication;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.Application.Publication;

public sealed class PruneReleasesTests
{
    [Fact]
    public async Task KeepsTheThirtyHighestReleasesPlusTheActiveOneSoRecentRollbacksStayPossible()
    {
        var scenario = Seed(35, active: 2);

        await Prune(scenario).ExecuteAsync();

        Assert.Equal(["dir-1", "dir-3", "dir-4", "dir-5"], scenario.Releases.Deleted.Order().ToArray());
    }

    [Fact]
    public async Task NeverDeletesTheReleaseTheCurrentLinkServesEvenWhenItIsOldSoVisitorsKeepTheirSite()
    {
        var scenario = Seed(35, active: 35);
        scenario.Releases.Active = "dir-3";

        await Prune(scenario).ExecuteAsync();

        Assert.DoesNotContain("dir-3", scenario.Releases.Deleted);
        Assert.Contains("dir-4", scenario.Releases.Deleted);
    }

    [Fact]
    public async Task AFailedDeleteIsLoggedNotThrownSoPruningNeverBlocksAPublish()
    {
        var scenario = Seed(35, active: 35);
        scenario.Releases.FailOnDelete = new IOException("Directory is busy.");

        await Prune(scenario).ExecuteAsync();

        Assert.Empty(scenario.Releases.Deleted);
    }

    [Fact]
    public async Task APublishSucceedsWhenThePruneFailsBecauseOldFilesAreOnlyDiskUse()
    {
        var scenario = new PublicationScenario();
        var draft = scenario.AddDraft('a');
        scenario.Publications.FailOnPrune = new InvalidOperationException("Store is down.");

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", draft));

        Assert.Equal(summary.DirectoryName, scenario.Releases.Active);
    }

    private static PruneReleases Prune(PublicationScenario scenario) =>
        new(scenario.Publications, scenario.Releases, NullLogger<PruneReleases>.Instance);

    private static PublicationScenario Seed(int count, int active)
    {
        var scenario = new PublicationScenario();
        for (var number = 1; number <= count; number++)
            scenario.Publications.Committed.Add(new(number, $"dir-{number}", DateTimeOffset.UnixEpoch, 1));
        scenario.Publications.ActiveReleaseNumber = active;
        scenario.Releases.Active = $"dir-{active}";
        return scenario;
    }
}
