using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishFirstPublishedTests
{
    [Fact]
    public async Task ARecordPublishedAgainAfterARollbackKeepsItsFirstTimeInItsFileItsManifestEntryAndTheTable()
    {
        var scenario = new PublicationScenario();
        var first = scenario.AddDraft('a');
        var one = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", first));
        scenario.Clock.Now = scenario.Clock.Now.AddDays(1);
        var rolledBack = scenario.AddDraft('b');
        var rolledBackTime = scenario.Clock.Now;
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", rolledBack));
        await scenario.Activator().ExecuteAsync(PublicationScenario.Owner, one.ReleaseNumber);
        scenario.Clock.Now = rolledBackTime.AddDays(7);

        var three = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k3", rolledBack));

        var reference = new PublishedRecordReference(PublishedRecordKind.DocumentChange, rolledBack);
        var manifest = JsonSerializer.Deserialize<PublicationRelease>(
            scenario.Releases.Directories[three.DirectoryName][PublicationProtocol.ManifestPath], PublicationProtocol.JsonOptions)!;
        var record = JsonSerializer.Deserialize<PublishedDocumentChange>(
            scenario.Releases.Directories[three.DirectoryName][PublicationProtocol.RecordDataPath(rolledBack)], PublicationProtocol.JsonOptions)!;
        Assert.Equal(rolledBackTime, manifest.Records.Single(entry => entry.RecordId == rolledBack).FirstPublishedAtUtc);
        Assert.Equal(rolledBackTime, record.FirstPublishedAtUtc);
        Assert.Equal(rolledBackTime, scenario.Publications.FirstPublished[reference]);
    }

    [Fact]
    public async Task ARecordThatTheActiveReleaseHoldsButTheTableLacksRefusesThePublishUntilTheBackfillRuns()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        var firstTime = scenario.Clock.Now;
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k1", action));
        scenario.Publications.FirstPublished.Clear();
        scenario.ReviseAction(action, 2);
        scenario.Clock.Now = firstTime.AddDays(3);

        var error = await Assert.ThrowsAsync<FirstPublishedMissingException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action)));
        await new BackfillFirstPublished(scenario.Publications, scenario.Releases).ExecuteAsync(PublicationScenario.Owner);
        var two = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action));

        Assert.Contains("releases first-published backfill", error.Message, StringComparison.Ordinal);
        var manifest = JsonSerializer.Deserialize<PublicationRelease>(
            scenario.Releases.Directories[two.DirectoryName][PublicationProtocol.ManifestPath], PublicationProtocol.JsonOptions)!;
        Assert.Equal(firstTime, Assert.Single(manifest.Records).FirstPublishedAtUtc);
    }

    [Fact]
    public async Task ARecordWithNoRowAndNoActiveEntryIsNewSoItTakesTheClockAndTheCommitCarriesItsRow()
    {
        var scenario = new PublicationScenario();
        var draft = scenario.AddDraft('a');

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", draft));

        var row = Assert.Single(scenario.Publications.LastCommit!.FirstPublications);
        Assert.Equal(new PublishedFirstPublication(PublishedRecordKind.DocumentChange, draft, scenario.Clock.Now), row);
    }

    [Fact]
    public async Task TheBackfillCopiesEveryRecordOfTheActiveManifestOnceAndCountsWhatWasAlreadyThere()
    {
        var scenario = new PublicationScenario();
        var kept = scenario.AddDraft('a');
        var other = scenario.AddDraft('b');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", kept, other));
        scenario.Publications.FirstPublished.Remove(new(PublishedRecordKind.DocumentChange, other));
        var backfill = new BackfillFirstPublished(scenario.Publications, scenario.Releases);

        var first = await backfill.ExecuteAsync(PublicationScenario.Owner);
        var second = await backfill.ExecuteAsync(PublicationScenario.Owner);

        Assert.Equal(new FirstPublishedBackfillResult(2, 1, 1), first);
        Assert.Equal(new FirstPublishedBackfillResult(2, 0, 2), second);
        Assert.Equal(scenario.Clock.Now, scenario.Publications.FirstPublished[new(PublishedRecordKind.DocumentChange, other)]);
    }

    [Fact]
    public async Task TheBackfillOfASiteWithNoActiveReleaseDoesNothingAndOnlyAnOwnerRunsIt()
    {
        var scenario = new PublicationScenario();
        var backfill = new BackfillFirstPublished(scenario.Publications, scenario.Releases);

        Assert.Equal(new FirstPublishedBackfillResult(0, 0, 0), await backfill.ExecuteAsync(PublicationScenario.Owner));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => backfill.ExecuteAsync(PublicationScenario.Reviewer));
    }

    private static PublishReleaseRequest Request(string key, string action) =>
        new([new PublishedRecordReference(PublishedRecordKind.Action, action)], key);
}
