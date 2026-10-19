using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishActionTests
{
    [Fact]
    public async Task AnActionFillsItsSearchFiltersFromTheRevisionItPublishesAndNamesOnlyItsSponsor()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", action));

        var entry = Assert.Single(ReadManifest(scenario, summary.DirectoryName).Records);
        Assert.Equal(PublishedRecordKind.Action, entry.Kind);
        Assert.Equal(("mayor", "Mayor Example"), (Assert.Single(entry.Officials).Id, entry.Officials[0].Name));
        Assert.Equal(["housing"], entry.IssueIds);
        Assert.Equal(["housing-development"], entry.PolicyAreaIds);
        Assert.Equal("api.congress.gov", entry.SourceHost);
        Assert.Equal(new DateOnly(2026, 1, 20), entry.EventDate);
    }

    [Fact]
    public async Task APersonWrittenActionPublishesWithPersonTextOriginSoThePageNeverCallsItAnAiSummary()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c', personText: true);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", action));

        var record = JsonSerializer.Deserialize<PublishedAction>(
            scenario.Releases.Directories[summary.DirectoryName][PublicationProtocol.RecordDataPath(action)], PublicationProtocol.JsonOptions)!;
        Assert.Equal(PublishedTextOrigin.Person, record.TextOrigin);
    }

    [Fact]
    public async Task AnActionWithoutAStoredAreaIDPublishesNoAreaFilterBecauseTheReviewerApprovedNone()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c', policyAreaId: null);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", action));

        Assert.Empty(Assert.Single(ReadManifest(scenario, summary.DirectoryName).Records).PolicyAreaIds);
    }

    [Fact]
    public async Task TheRecordEmbedsEachCitedApiPageOnceAndNamesEveryOfficialByConfiguredName()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", action));

        var files = scenario.Releases.Directories[summary.DirectoryName];
        Assert.Contains(PublicationProtocol.RecordPagePath(action), files.Keys);
        var record = JsonSerializer.Deserialize<PublishedAction>(files[PublicationProtocol.RecordDataPath(action)], PublicationProtocol.JsonOptions)!;
        Assert.Equal([PublicationScenario.ActionDetail, PublicationScenario.ActionCosponsors], record.Sources.Select(source => source.Text));
        Assert.Equal(PublishedTextOrigin.Model, record.TextOrigin);
        Assert.Equal(["Mayor Example", "Council Chair Example"], record.Sponsorships.Select(item => item.Official.Name));
        Assert.Equal("Sponsor: M000001", record.Sponsorships[0].Citations[0].Quote);
        Assert.Equal("Policy area: Housing and Community Development", record.PolicyAreaCitation!.Quote);
        Assert.Equal(PublicationScenario.ApprovedAt, record.ApprovedAtUtc);
    }

    [Fact]
    public async Task CommitBindsTheActionToItsKindAndReviewStateSoTheStoreChecksTheActionTable()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", action));

        var commit = scenario.Publications.LastCommit!;
        Assert.Equal(new PublishedRevisionBinding(PublishedRecordKind.Action, action, 1), Assert.Single(commit.Records));
        Assert.Equal(new ReviewStateExpectation(PublishedRecordKind.Action, action, 1, 1), Assert.Single(commit.AddedRecords));
    }

    [Fact]
    public async Task ARequestWithOnlyActionsNeverAsksTheOtherStores()
    {
        var scenario = new PublicationScenario();

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", scenario.AddAction('c')));

        Assert.Equal((0, 0), (scenario.Reviews.ReadCount, scenario.Statements.PublicationReadCount));
    }

    [Fact]
    public async Task ActionAlreadyPublishedAtItsCurrentRevisionIsRejectedSoReleasesDoNotRepeatIt()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k1", action));

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action)));

        Assert.Equal($"Action {action} is already published at its current revision.", error.Message);
        Assert.Single(scenario.Publications.Committed);
    }

    [Fact]
    public async Task ActionThatIsNotApprovedIsRefusedBecauseNoModelOrDraftMaySpeakForTheSite()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c', decisions: [PublicationScenario.Decision(new string('c', 64), 1, ReviewDecisionKind.RequestChanges)]);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", action)));

        Assert.Equal($"Action {action} is not approved or has unresolved concerns.", error.Message);
        Assert.Equal(0, scenario.Releases.BeginCount);
    }

    [Fact]
    public async Task ARepublishedActionReplacesItsCarriedEntryInsteadOfDuplicatingIt()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        var firstPublishedAt = scenario.Clock.Now;
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k1", action));
        var review = scenario.Actions.Publishable[action].Review;
        var revised = review.CurrentRevision with { RevisionNumber = 2, TrackedIssueIds = ["transit"] };
        scenario.Actions.Publishable[action] = scenario.Actions.Publishable[action] with
        {
            Review = review with
            {
                CurrentRevision = revised,
                Revisions = review.Revisions.Add(revised),
                Decisions = review.Decisions.Add(PublicationScenario.Decision(action, 2, ReviewDecisionKind.Approve) with { RevisionNumber = 2 }),
                ReviewStateVersion = 2
            }
        };

        scenario.Clock.Now = firstPublishedAt.AddDays(1);

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action));

        var entry = Assert.Single(ReadManifest(scenario, second.DirectoryName).Records);
        Assert.Equal(2, entry.RevisionNumber);
        Assert.Equal(["transit"], entry.IssueIds);
        Assert.Equal(firstPublishedAt, entry.FirstPublishedAtUtc);
        Assert.Empty(scenario.Releases.Links);
    }

    [Fact]
    public async Task AnActionAndAStatementCannotShareOneIdBecauseTheyWouldShareOneRecordFile()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('c');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Statement, statement)));
        var action = scenario.AddAction('c');

        await Assert.ThrowsAsync<InvalidDataException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action)));

        Assert.Equal(1, scenario.Releases.BeginCount);
    }

    private static PublishReleaseRequest Request(string key, string actionId) =>
        PublicationScenario.Request(key, new PublishedRecordReference(PublishedRecordKind.Action, actionId));

    private static PublicationRelease ReadManifest(PublicationScenario scenario, string directory) =>
        JsonSerializer.Deserialize<PublicationRelease>(
            scenario.Releases.Directories[directory][PublicationProtocol.ManifestPath], PublicationProtocol.JsonOptions)!;
}
