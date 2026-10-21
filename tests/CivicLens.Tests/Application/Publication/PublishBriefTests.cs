using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishBriefTests
{
    private static readonly DateOnly Week = BriefFixtures.Week;

    [Fact]
    public async Task ABriefFillsOnlyItsWeekAsTheEventDateAndCopiesTheHeadlineOfEachCitedRecord()
    {
        var scenario = new PublicationScenario();
        var statement = await PublishStatementAsync(scenario);
        var brief = scenario.AddBrief(Week, [Cite(statement, 1)]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", brief));

        var entry = ReadManifest(scenario, summary.DirectoryName).Records.Single(candidate => candidate.RecordId == brief);
        Assert.Equal((PublishedRecordKind.Brief, Week), (entry.Kind, entry.EventDate));
        Assert.Empty(entry.Officials);
        Assert.Empty(entry.IssueIds);
        Assert.Empty(entry.PolicyAreaIds);
        Assert.Null(entry.SourceHost);
        var files = scenario.Releases.Directories[summary.DirectoryName];
        var record = JsonSerializer.Deserialize<PublishedBrief>(files[PublicationProtocol.RecordDataPath(brief)], PublicationProtocol.JsonOptions)!;
        var citation = Assert.Single(record.Citations);
        Assert.Equal(("R1", PublishedRecordKind.Statement, statement, "Cited R1"), (citation.Id, citation.Kind, citation.RecordId, citation.Headline));
        Assert.Equal(PublicationScenario.ApprovedAt, record.ApprovedAtUtc);
        Assert.Contains(PublicationProtocol.RecordPagePath(brief), files.Keys);
    }

    [Fact]
    public async Task CommitBindsTheBriefToItsKindAndReviewStateSoTheStoreChecksTheBriefTable()
    {
        var scenario = new PublicationScenario();
        var statement = await PublishStatementAsync(scenario);
        var brief = scenario.AddBrief(Week, [Cite(statement, 1)]);

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", brief));

        var commit = scenario.Publications.LastCommit!;
        Assert.Equal(new ReviewStateExpectation(PublishedRecordKind.Brief, brief, 1, 1), Assert.Single(commit.AddedRecords));
        Assert.Contains(new PublishedRevisionBinding(PublishedRecordKind.Brief, brief, 1), commit.Records);
    }

    [Fact]
    public async Task ABriefIsRefusedWhenTheNewReleaseDoesNotHoldTheCitedRecordBecauseThePageWouldLinkToNothing()
    {
        var scenario = new PublicationScenario();
        var brief = scenario.AddBrief(Week, [Cite(new string('c', 64), 1)]);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", brief)));

        Assert.Contains($"Brief {brief} cites a record", error.Message);
        Assert.Equal(0, scenario.Releases.BeginCount);
    }

    [Fact]
    public async Task ABriefIsRefusedWhenTheSamePublishReplacesItsCitedRecordWithANewerRevision()
    {
        var scenario = new PublicationScenario();
        var statement = await PublishStatementAsync(scenario);
        scenario.AddStatement('c', revisionNumber: 2);
        var brief = scenario.AddBrief(Week, [Cite(statement, 1)]);

        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.WithNotesFor(
                PublicationScenario.Request("k2", Reference(PublishedRecordKind.Statement, statement), Reference(PublishedRecordKind.Brief, brief)),
                Reference(PublishedRecordKind.Statement, statement))));

        Assert.Equal(1, scenario.Releases.BeginCount);
    }

    [Fact]
    public async Task ABriefPublishesWithTheCitedRecordThatTheSamePublishAdds()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('c');
        var brief = scenario.AddBrief(Week, [Cite(statement, 1)]);

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", Reference(PublishedRecordKind.Brief, brief), Reference(PublishedRecordKind.Statement, statement)));

        Assert.Contains(new PublishedRevisionBinding(PublishedRecordKind.Statement, statement, 1), scenario.Briefs.ReleaseRecordsAtLastRead);
        Assert.Equal(2, scenario.Publications.LastCommit!.Records.Length);
    }

    [Fact]
    public async Task ABriefIsCheckedAgainstTheRevisionThatTheSamePublishPutsInTheNewRelease()
    {
        var scenario = new PublicationScenario();
        var statement = await PublishStatementAsync(scenario);
        scenario.AddStatement('c', revisionNumber: 2);
        var brief = scenario.AddBrief(Week, [Cite(statement, 2)]);

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.WithNotesFor(
            PublicationScenario.Request("k2", Reference(PublishedRecordKind.Statement, statement), Reference(PublishedRecordKind.Brief, brief)),
            Reference(PublishedRecordKind.Statement, statement)));

        Assert.Equal([new PublishedRevisionBinding(PublishedRecordKind.Statement, statement, 2)],
            scenario.Briefs.ReleaseRecordsAtLastRead.Where(binding => binding.Kind == PublishedRecordKind.Statement));
    }

    [Fact]
    public async Task ABriefThatIsNotApprovedIsRefusedBecauseNoModelOrDraftMaySpeakForTheSite()
    {
        var scenario = new PublicationScenario();
        var statement = await PublishStatementAsync(scenario);
        var brief = scenario.AddBrief(Week, [Cite(statement, 1)], ReviewDecisionKind.RequestChanges);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", brief)));

        Assert.Equal($"Brief {brief} is not approved or has unresolved concerns.", error.Message);
    }

    [Fact]
    public async Task ABriefMustHaveTheShapeOfABriefIdSoItCannotNameAShortDocumentChangeId()
    {
        var scenario = new PublicationScenario();

        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            Request("k", new string('a', 32))));
    }

    private static async Task<string> PublishStatementAsync(PublicationScenario scenario)
    {
        var statement = scenario.AddStatement('c');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k1", Reference(PublishedRecordKind.Statement, statement)));
        return statement;
    }

    private static PublishedRevisionBinding Cite(string statementId, int revision) =>
        new(PublishedRecordKind.Statement, statementId, revision);

    private static PublishedRecordReference Reference(PublishedRecordKind kind, string id) => new(kind, id);

    private static PublishReleaseRequest Request(string key, string briefId) =>
        PublicationScenario.Request(key, Reference(PublishedRecordKind.Brief, briefId));

    private static PublicationRelease ReadManifest(PublicationScenario scenario, string directory) =>
        JsonSerializer.Deserialize<PublicationRelease>(
            scenario.Releases.Directories[directory][PublicationProtocol.ManifestPath], PublicationProtocol.JsonOptions)!;
}
