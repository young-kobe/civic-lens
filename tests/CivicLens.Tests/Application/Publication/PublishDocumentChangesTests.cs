using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishDocumentChangesTests
{
    [Fact]
    public async Task ReviewerCannotPublishBecauseOnlyTheOwnerSpeaksForTheSite()
    {
        var scenario = new PublicationScenario();
        var draft = scenario.AddDraft('a');

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Reviewer, PublicationScenario.Request("k", draft)));

        Assert.Equal(0, scenario.Releases.BeginCount);
        Assert.Empty(scenario.Publications.Committed);
    }

    [Fact]
    public async Task PublishesApprovedDraftWithExactQuoteAndBindsTheReviewedState()
    {
        var scenario = new PublicationScenario();
        var draft = scenario.AddDraft('a', officialIds: ["mayor"]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", draft));

        Assert.Equal(1, summary.ReleaseNumber);
        Assert.Equal(summary.DirectoryName, scenario.Releases.Active);
        var record = ReadRecord(scenario, summary.DirectoryName, draft);
        Assert.Equal("old a", Assert.Single(record.Citations).Quote);
        Assert.Equal("Mayor Example", Assert.Single(record.Officials).Name);
        Assert.Equal(PublicationScenario.ApprovedAt, record.ApprovedAtUtc);
        Assert.Equal(scenario.Clock.Now, record.FirstPublishedAtUtc);
        Assert.Equal(new PublishedRevisionBinding(draft, 1), Assert.Single(scenario.Publications.LastCommit!.Records));
        Assert.Equal(new ReviewStateExpectation(draft, 1, 1), Assert.Single(scenario.Publications.LastCommit.AddedRecords));
    }

    [Fact]
    public async Task PublishingSeveralDraftsReadsReviewsAndEvidenceOnceSoCostDoesNotGrowPerDraft()
    {
        var scenario = new PublicationScenario();
        var first = scenario.AddDraft('a');
        var second = scenario.AddDraft('b');
        var third = scenario.AddDraft('c');

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", third, first, second));

        Assert.Equal(1, scenario.Reviews.ReadCount);
        Assert.Equal([third, first, second], scenario.Publications.LastCommit!.AddedRecords.Select(added => added.DraftId));
    }

    [Fact]
    public async Task UnknownDraftFailsThePublishSoATypoCannotSilentlyShrinkTheRelease()
    {
        var scenario = new PublicationScenario();
        var draft = scenario.AddDraft('a');
        var missing = new string('f', 32);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", draft, missing)));

        Assert.Contains($"Draft {missing} does not exist.", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, scenario.Releases.BeginCount);
    }

    [Fact]
    public async Task ManifestIsWrittenLastSoAnIncompleteDirectoryNeverLooksComplete()
    {
        var scenario = new PublicationScenario();
        var draft = scenario.AddDraft('a');

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", draft));

        var order = scenario.Releases.WriteOrder;
        Assert.Equal(PublicationProtocol.ManifestPath, order[^1]);
        Assert.Equal(PublicationProtocol.IndexPagePath(1), order[^2]);
        Assert.True(order.IndexOf(PublicationProtocol.RecordDataPath(draft)) < order.IndexOf("assets/site.css"));
    }

    [Fact]
    public async Task EveryIndexPageIsWrittenBeforeTheManifestSoALongReleaseIsNeverTruncated()
    {
        var scenario = new PublicationScenario();
        scenario.SeedRelease(119, withFiles: true);
        var draft = scenario.AddDraft('a');
        scenario.Releases.WriteOrder.Clear();

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", draft));

        Assert.Equal(120, summary.RecordCount);
        var order = scenario.Releases.WriteOrder;
        Assert.Equal(
            [PublicationProtocol.IndexPagePath(1), "page-2.html", "page-3.html", PublicationProtocol.ManifestPath],
            order.TakeLast(4));
        Assert.Equal("index.html", PublicationProtocol.IndexPagePath(1));
        Assert.DoesNotContain("page-4.html", order);
        Assert.Contains("<html>index 3 120</html>", System.Text.Encoding.UTF8.GetString(
            scenario.Releases.Directories[summary.DirectoryName]["page-3.html"]));
    }

    [Fact]
    public async Task RejectsMoreRecordsThanTheCapBeforeAnythingIsBuilt()
    {
        var scenario = new PublicationScenario();
        scenario.SeedRelease(PublicationProtocol.MaximumRecordsPerRelease, withFiles: false);
        var draft = scenario.AddDraft('a');

        await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", draft)));

        Assert.Equal(0, scenario.Releases.BeginCount);
        Assert.Single(scenario.Publications.Committed);
        Assert.Empty(scenario.Releases.Activated);
    }

    [Theory]
    [InlineData("awaiting")]
    [InlineData("changes-requested")]
    [InlineData("unresolved-concern")]
    public async Task RejectsDraftsThatAreNotCleanlyApproved(string scenarioName)
    {
        var scenario = new PublicationScenario();
        var id = new string('a', 32);
        var decisions = scenarioName switch
        {
            "awaiting" => System.Collections.Immutable.ImmutableArray<ReviewDecision>.Empty,
            "changes-requested" => [PublicationScenario.Decision(id, 1, ReviewDecisionKind.RequestChanges)],
            _ => [PublicationScenario.Decision(id, 1, ReviewDecisionKind.RequestChanges),
                PublicationScenario.Decision(id, 2, ReviewDecisionKind.Approve)]
        };
        var draft = scenario.AddDraft('a', decisions);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", draft)));

        Assert.Equal(0, scenario.Releases.BeginCount);
        Assert.Empty(scenario.Releases.Activated);
    }

    [Fact]
    public async Task RejectsOfficialWithoutConfiguredPublicNameAndLeavesNoDirectory()
    {
        var scenario = new PublicationScenario();
        var draft = scenario.AddDraft('a', officialIds: ["unnamed"]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", draft)));

        Assert.Empty(scenario.Releases.Directories);
        Assert.Equal(1, scenario.Releases.DiscardedStagings);
    }

    [Fact]
    public async Task RejectsCitationThatDoesNotFitTheExtractedTextRatherThanPublishingAWrongQuote()
    {
        var scenario = new PublicationScenario();
        var draft = scenario.AddDraft('a', citationStart: 500);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", draft)));

        Assert.Empty(scenario.Releases.Directories);
        Assert.Empty(scenario.Publications.Committed);
    }

    [Fact]
    public async Task CommitConflictDeletesTheBuiltDirectoryAndActivatesNothing()
    {
        var scenario = new PublicationScenario();
        var draft = scenario.AddDraft('a');
        scenario.Publications.ConflictOnCommit = true;

        await Assert.ThrowsAsync<PublicationConflictException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", draft)));

        Assert.Single(scenario.Releases.Deleted);
        Assert.Empty(scenario.Releases.Directories);
        Assert.Empty(scenario.Releases.Activated);
    }

    [Fact]
    public async Task InvalidCommitDeletesTheBuiltDirectoryBecauseNothingWasRecorded()
    {
        var scenario = new PublicationScenario();
        var draft = scenario.AddDraft('a');
        scenario.Publications.FailOnCommit = new ArgumentException("Invalid commit.");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", draft)));

        Assert.Single(scenario.Releases.Deleted);
        Assert.Empty(scenario.Releases.Directories);
        Assert.Empty(scenario.Releases.Activated);
    }

    [Fact]
    public async Task ConcurrentReplayServesTheActiveReleaseRatherThanRollingBackToTheWinner()
    {
        var scenario = new PublicationScenario();
        var winner = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", scenario.AddDraft('b')));
        var newer = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('c')));
        scenario.Publications.ConcurrentWinner = winner;
        scenario.Releases.Activated.Clear();

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k3", scenario.AddDraft('a')));

        Assert.Equal(winner, summary);
        var built = Assert.Single(scenario.Releases.Deleted);
        Assert.DoesNotContain(built, scenario.Releases.Directories.Keys);
        Assert.Equal([newer.DirectoryName], scenario.Releases.Activated);
    }

    [Fact]
    public async Task PublishServesTheRecordedActiveReleaseSoAnOlderPublishCannotUndoANewerActivation()
    {
        var scenario = new PublicationScenario();
        scenario.SeedRelease(1, withFiles: true);
        scenario.Publications.BeforeServe = () => scenario.Publications.ActiveReleaseNumber = 1;

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", scenario.AddDraft('a')));

        Assert.Equal(2, summary.ReleaseNumber);
        Assert.Equal(["seeded"], scenario.Releases.Activated);
    }

    [Fact]
    public async Task RetryAfterAFailedActivationServesTheCommittedRelease()
    {
        var scenario = new PublicationScenario();
        var request = PublicationScenario.Request("k", scenario.AddDraft('a'));
        scenario.Releases.FailOnActivate = new IOException("Link rename failed.");
        await Assert.ThrowsAsync<IOException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, request));
        scenario.Releases.FailOnActivate = null;

        var replay = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, request);

        Assert.Equal(replay.DirectoryName, scenario.Releases.Active);
        Assert.Single(scenario.Publications.Committed);
    }

    [Fact]
    public async Task PublishingAfterAFailedActivationKeepsTheCommittedRecordsBecauseNobodyRolledBack()
    {
        var scenario = new PublicationScenario();
        var first = scenario.AddDraft('a');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", first));
        var second = scenario.AddDraft('b');
        scenario.Releases.FailOnActivate = new IOException("Link rename failed.");
        await Assert.ThrowsAsync<IOException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", second)));
        scenario.Releases.FailOnActivate = null;
        var third = scenario.AddDraft('c');

        var three = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k3", third));

        Assert.Equal([first, second, third], ReadManifest(scenario, three.DirectoryName).Records.Select(entry => entry.RecordId));
        Assert.Equal(three.DirectoryName, scenario.Releases.Active);
    }

    [Fact]
    public async Task CleanupFailureDoesNotHideTheConflictThatCausedIt()
    {
        var scenario = new PublicationScenario();
        var draft = scenario.AddDraft('a');
        scenario.Publications.ConflictOnCommit = true;
        scenario.Releases.FailOnDelete = new IOException("Directory is busy.");

        await Assert.ThrowsAsync<PublicationConflictException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", draft)));

        Assert.Empty(scenario.Releases.Activated);
    }

    [Fact]
    public async Task PublishingAfterRollbackBuildsOnTheActiveReleaseSoRolledBackRecordsStayWithdrawn()
    {
        var scenario = new PublicationScenario();
        var first = scenario.AddDraft('a');
        var firstTime = scenario.Clock.Now;
        var one = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", first));
        var second = scenario.AddDraft('b');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", second));
        await new ActivatePublicationRelease(scenario.Publications, scenario.Releases)
            .ExecuteAsync(PublicationScenario.Owner, one.ReleaseNumber);
        scenario.Clock.Now = firstTime.AddDays(7);
        var third = scenario.AddDraft('c');
        scenario.Releases.Links.Clear();

        var three = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k3", third));

        Assert.Equal(3, three.ReleaseNumber);
        Assert.Equal(one.ReleaseNumber, scenario.Publications.LastCommit!.BaseReleaseNumber);
        var manifest = ReadManifest(scenario, three.DirectoryName);
        Assert.Equal([third, first], manifest.Records.Select(entry => entry.RecordId));
        Assert.Equal(firstTime, manifest.Records[1].FirstPublishedAtUtc);
        Assert.All(scenario.Releases.Links, link => Assert.Equal(one.DirectoryName, link.SourceDirectory));
        Assert.Equal(2, scenario.Releases.Links.Count);
    }

    [Fact]
    public async Task ActiveReleaseWhoseManifestNamesAnotherReleaseIsRejectedBeforeAnythingIsBuilt()
    {
        var scenario = new PublicationScenario();
        var one = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", scenario.AddDraft('a')));
        var two = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('b')));
        scenario.Releases.Directories[two.DirectoryName][PublicationProtocol.ManifestPath] =
            scenario.Releases.Directories[one.DirectoryName][PublicationProtocol.ManifestPath];
        var begins = scenario.Releases.BeginCount;

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k3", scenario.AddDraft('c'))));

        Assert.Equal(begins, scenario.Releases.BeginCount);
    }

    [Fact]
    public async Task ReplayReturnsTheOriginalReleaseWithoutBuildingAnotherOne()
    {
        var scenario = new PublicationScenario();
        var draft = scenario.AddDraft('a');
        var request = PublicationScenario.Request("k", draft);
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, request);
        var begins = scenario.Releases.BeginCount;
        var reads = scenario.Reviews.ReadCount;

        var replay = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, request);

        Assert.Equal(first, replay);
        Assert.Equal(begins, scenario.Releases.BeginCount);
        Assert.Equal(reads, scenario.Reviews.ReadCount);
        Assert.Single(scenario.Publications.Committed);
    }

    [Fact]
    public async Task SecondReleaseCarriesForwardTheFirstRecordWithItsOriginalFirstPublishedDate()
    {
        var scenario = new PublicationScenario();
        var first = scenario.AddDraft('a');
        var firstTime = scenario.Clock.Now;
        var one = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", first));
        var originalJson = scenario.Releases.Directories[one.DirectoryName][PublicationProtocol.RecordDataPath(first)];
        scenario.Clock.Now = firstTime.AddDays(7);
        var second = scenario.AddDraft('b');
        scenario.Releases.WriteOrder.Clear();
        scenario.Releases.ReadPaths.Clear();

        var two = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", second));

        Assert.Equal(2, two.ReleaseNumber);
        Assert.Equal(2, two.RecordCount);
        var manifest = ReadManifest(scenario, two.DirectoryName);
        Assert.Equal([second, first], manifest.Records.Select(entry => entry.RecordId));
        Assert.Equal(firstTime, manifest.Records[1].FirstPublishedAtUtc);
        Assert.Equal(originalJson, scenario.Releases.Directories[two.DirectoryName][PublicationProtocol.RecordDataPath(first)]);
        Assert.Equal(
            [(one.DirectoryName, PublicationProtocol.RecordDataPath(first)), (one.DirectoryName, PublicationProtocol.RecordPagePath(first))],
            scenario.Releases.Links.TakeLast(2));
        Assert.DoesNotContain(PublicationProtocol.RecordDataPath(first), scenario.Releases.WriteOrder);
        Assert.DoesNotContain(PublicationProtocol.RecordPagePath(first), scenario.Releases.WriteOrder);
        Assert.Contains(PublicationProtocol.RecordDataPath(second), scenario.Releases.WriteOrder);
        Assert.Contains(PublicationProtocol.RecordPagePath(second), scenario.Releases.WriteOrder);
        Assert.Equal([PublicationProtocol.ManifestPath], scenario.Releases.ReadPaths);
        Assert.Equal(new ReviewStateExpectation(second, 1, 1), Assert.Single(scenario.Publications.LastCommit!.AddedRecords));
        Assert.Equal(2, scenario.Publications.LastCommit.Records.Length);
    }

    [Fact]
    public async Task RejectsRepublishingTheSameRevisionBecauseTheReleaseWouldChangeNothing()
    {
        var scenario = new PublicationScenario();
        var draft = scenario.AddDraft('a');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", draft));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", draft)));

        Assert.Single(scenario.Publications.Committed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65)]
    public async Task RejectsEmptyOrOversizedSelections(int count)
    {
        var scenario = new PublicationScenario();
        var ids = Enumerable.Range(0, count).Select(i => i.ToString("x32")).ToArray();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", ids)));
    }

    [Fact]
    public async Task RejectsDuplicateDraftIds()
    {
        var scenario = new PublicationScenario();
        var draft = scenario.AddDraft('a');

        await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", draft, draft)));
    }

    private static PublishedDocumentChange ReadRecord(PublicationScenario scenario, string directory, string draftId) =>
        JsonSerializer.Deserialize<PublishedDocumentChange>(
            scenario.Releases.Directories[directory][PublicationProtocol.RecordDataPath(draftId)],
            PublicationProtocol.JsonOptions)!;

    private static PublicationRelease ReadManifest(PublicationScenario scenario, string directory) =>
        JsonSerializer.Deserialize<PublicationRelease>(
            scenario.Releases.Directories[directory][PublicationProtocol.ManifestPath], PublicationProtocol.JsonOptions)!;
}
