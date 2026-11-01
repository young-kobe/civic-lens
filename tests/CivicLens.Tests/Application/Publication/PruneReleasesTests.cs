using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.Application.Publication;

public sealed class PruneReleasesTests
{
    [Fact]
    public async Task RetiresReleasesBelowTheKeptNumbersButNotTheActiveOneSoARollbackTargetSurvives()
    {
        var scenario = Seed(35, active: 2);

        await Prune(scenario).ExecuteAsync();

        Assert.Equal(["release-1", "release-3", "release-4", "release-5"], scenario.Releases.RetiredEver.Order().ToArray());
    }

    [Fact]
    public async Task AsksTheStoreToKeepThirtyReleases()
    {
        var scenario = Seed(1, active: 1);

        await Prune(scenario).ExecuteAsync();

        Assert.Equal(30, scenario.Publications.PrunedKeep);
    }

    [Fact]
    public async Task NeverRetiresTheReleaseTheCurrentLinkServesEvenWhenItIsOldSoVisitorsKeepTheirSite()
    {
        var scenario = Seed(35, active: 35);
        scenario.Releases.Active = "release-3";

        await Prune(scenario).ExecuteAsync();

        Assert.DoesNotContain("release-3", scenario.Releases.RetiredEver);
        Assert.Contains("release-4", scenario.Releases.RetiredEver);
    }

    [Fact]
    public async Task AFolderWithNoRowIsRetiredToo()
    {
        var scenario = Seed(35, active: 35);
        scenario.Releases.Directories["release-2-orphan"] = [];

        await Prune(scenario).ExecuteAsync();

        Assert.Contains("release-2-orphan", scenario.Releases.RetiredEver);
    }

    [Fact]
    public async Task ARetiredFolderIsDeletedAfterTheStoreLockEndsSoReviewWritersAreNotBlockedByFileWork()
    {
        var scenario = Seed(35, active: 35);
        var deletedInsideTheLock = true;
        scenario.Releases.OnDeleteRetired = () => deletedInsideTheLock = scenario.Publications.InsidePrune;

        await Prune(scenario).ExecuteAsync();

        Assert.False(deletedInsideTheLock);
        Assert.Equal(5, scenario.Releases.RetiredEver.Count);
        Assert.Empty(scenario.Releases.Retired);
    }

    [Fact]
    public async Task AFailedDeleteIsLoggedNotThrownSoPruningNeverBlocksAPublish()
    {
        var scenario = Seed(35, active: 35);
        scenario.Releases.FailOnDeleteRetired = new IOException("Directory is busy.");

        await Prune(scenario).ExecuteAsync();

        Assert.NotEmpty(scenario.Releases.Retired);
    }

    [Fact]
    public async Task ShutdownAfterTheCommitDoesNotFailThePublishAndStillRetiresUnderTheLock()
    {
        var scenario = Seed(35, active: 35);
        using var shutdown = new CancellationTokenSource();
        await shutdown.CancelAsync();

        await Prune(scenario).ExecuteAsync(shutdown.Token);

        Assert.Equal(5, scenario.Releases.Retired.Count);
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

    [Fact]
    public async Task EveryPublishPrunesSoTheOldestFolderIsGoneOncePastTheKeepLimit()
    {
        var scenario = new PublicationScenario();
        var publisher = scenario.Publisher();
        string? first = null;
        for (var index = 0; index < PruneReleases.KeepReleases + 2; index++)
        {
            var id = "0123456789abcdef"[index % 16];
            var reference = index < 16
                ? new PublishedRecordReference(PublishedRecordKind.DocumentChange, scenario.AddDraft(id))
                : new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement(id));
            var summary = await publisher.ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request($"k{index}", reference));
            first ??= summary.DirectoryName;
        }

        Assert.DoesNotContain(first!, scenario.Releases.Directories.Keys);
        Assert.Equal(PruneReleases.KeepReleases, scenario.Releases.Directories.Count);
    }

    private static PruneReleases Prune(PublicationScenario scenario) =>
        new(scenario.Publications, scenario.Releases, NullLogger<PruneReleases>.Instance);

    private static PublicationScenario Seed(int count, int active)
    {
        var scenario = new PublicationScenario();
        for (var number = 1; number <= count; number++)
        {
            scenario.Publications.Committed.Add(new(number, $"release-{number}", DateTimeOffset.UnixEpoch, 1));
            scenario.Releases.Directories[$"release-{number}"] = [];
        }

        scenario.Publications.ActiveReleaseNumber = active;
        scenario.Releases.Active = $"release-{active}";
        return scenario;
    }
}
