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
    public async Task ManifestIsWrittenLastSoAnIncompleteDirectoryNeverLooksComplete()
    {
        var scenario = new PublicationScenario();
        var draft = scenario.AddDraft('a');

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", draft));

        var order = scenario.Releases.WriteOrder;
        Assert.Equal(PublicationProtocol.ManifestPath, order[^1]);
        Assert.Equal(PublicationProtocol.IndexPagePath, order[^2]);
        Assert.True(order.IndexOf(PublicationProtocol.RecordDataPath(draft)) < order.IndexOf("assets/site.css"));
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

        var two = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", second));

        Assert.Equal(2, two.ReleaseNumber);
        Assert.Equal(2, two.RecordCount);
        var manifest = ReadManifest(scenario, two.DirectoryName);
        Assert.Equal([second, first], manifest.Records.Select(entry => entry.RecordId));
        Assert.Equal(firstTime, manifest.Records[1].FirstPublishedAtUtc);
        Assert.Equal(originalJson, scenario.Releases.Directories[two.DirectoryName][PublicationProtocol.RecordDataPath(first)]);
        Assert.Contains(PublicationProtocol.RecordPagePath(first), scenario.Releases.Directories[two.DirectoryName].Keys);
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
