using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishRelationshipTests
{
    private static readonly DateOnly StatementDate = new(2026, 1, 15);

    [Fact]
    public async Task ARelationshipEntryTakesTheOfficialsFromTheStatementTheIssuesAndAreasFromBothSidesAndTheStatementDate()
    {
        var scenario = new PublicationScenario();
        var (statement, action, _) = await PublishRecordsAsync(scenario);
        var relationship = scenario.AddRelationship('e', statement, action);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", relationship));

        var entry = ReadManifest(scenario, summary.DirectoryName).Records.Single(candidate => candidate.RecordId == relationship);
        Assert.Equal((PublishedRecordKind.Relationship, StatementDate, "Statement c and Action d"), (entry.Kind, entry.EventDate, entry.Headline));
        Assert.Equal(["mayor"], entry.Officials.Select(official => official.Id));
        Assert.Equal(["housing"], entry.IssueIds);
        Assert.Equal(["land-use", "housing-development"], entry.PolicyAreaIds);
        Assert.Null(entry.SourceHost);
        Assert.Equal((PublishedRelationshipType.DescribesOwnBill, statement, PublishedRecordKind.Action, action),
            (entry.LinkType, entry.StatementRecordId, entry.TargetKind, entry.TargetRecordId));
    }

    [Theory]
    [InlineData(true, PublishedApprover.Code)]
    [InlineData(false, PublishedApprover.Person)]
    public async Task ARelationshipNamesWhoApprovedItInTheRecordFileAndTheEntry(bool approvedByCode, PublishedApprover expected)
    {
        var scenario = new PublicationScenario();
        var (statement, action, _) = await PublishRecordsAsync(scenario);
        var relationship = scenario.AddRelationship('e', statement, action, approvedByCode: approvedByCode);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", relationship));

        var record = JsonSerializer.Deserialize<PublishedRelationship>(
            scenario.Releases.Directories[summary.DirectoryName][PublicationProtocol.RecordDataPath(relationship)], PublicationProtocol.JsonOptions)!;
        Assert.Equal(expected, record.ApprovedBy);
        Assert.Equal(expected, ReadManifest(scenario, summary.DirectoryName).Records.Single(candidate => candidate.RecordId == relationship).ApprovedBy);
    }

    [Fact]
    public async Task TheActionEntryCarriesTheOfficialBillTitleSoALinkedBillShowsItWithoutReadingTheActionFile()
    {
        var scenario = new PublicationScenario();
        var (statement, action, _) = await PublishRecordsAsync(scenario);
        var relationship = scenario.AddRelationship('e', statement, action);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", relationship));

        var entry = ReadManifest(scenario, summary.DirectoryName).Records.Single(candidate => candidate.RecordId == action);

        Assert.Equal("A housing bill", entry.BillTitle);
    }

    [Fact]
    public async Task TheRecordFileNamesTheCitedRevisionsAndCopiesTheWordingThatAPersonApproved()
    {
        var scenario = new PublicationScenario();
        var (statement, action, _) = await PublishRecordsAsync(scenario);
        var relationship = scenario.AddRelationship('e', statement, action);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", relationship));

        var record = ReadRecord<PublishedRelationship>(scenario, summary.DirectoryName, relationship);
        Assert.Equal(new PublishedRelationshipCitation { Kind = PublishedRecordKind.Statement, RecordId = statement, RevisionNumber = 1, Headline = "Statement c" },
            record.Statement);
        Assert.Equal(new PublishedRelationshipCitation { Kind = PublishedRecordKind.Action, RecordId = action, RevisionNumber = 1, Headline = "Action d" },
            record.Target);
        Assert.Equal((PublishedRelationshipType.DescribesOwnBill, "Statement e describes the record.", "housing matters", "Title: A housing bill"),
            (record.Type, record.Reason, record.Quote, record.TargetFact));
        Assert.Equal(PublicationScenario.ApprovedAt, record.ApprovedAtUtc);
    }

    [Fact]
    public async Task CommitBindsTheRelationshipToItsKindAndReviewStateSoTheStoreChecksTheRelationshipTable()
    {
        var scenario = new PublicationScenario();
        var (statement, action, _) = await PublishRecordsAsync(scenario);
        var relationship = scenario.AddRelationship('e', statement, action);

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", relationship));

        var commit = scenario.Publications.LastCommit!;
        Assert.Equal(new ReviewStateExpectation(PublishedRecordKind.Relationship, relationship, 1, 1), Assert.Single(commit.AddedRecords));
        Assert.Contains(new PublishedRevisionBinding(PublishedRecordKind.Relationship, relationship, 1), commit.Records);
    }

    [Fact]
    public async Task ARelationshipIsRefusedWhenTheNewReleaseDoesNotHoldACitedRecordBecauseTheLinkWouldPointAtNothing()
    {
        var scenario = new PublicationScenario();
        var relationship = scenario.AddRelationship('e', scenario.AddStatement('c'), scenario.AddAction('d'));

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", relationship)));

        Assert.Contains($"Relationship {relationship} cites a record", error.Message);
        Assert.Equal(0, scenario.Releases.BeginCount);
    }

    [Fact]
    public async Task ARelationshipIsRefusedWhenTheSamePublishReplacesACitedRecordWithANewerRevision()
    {
        var scenario = new PublicationScenario();
        var (statement, action, _) = await PublishRecordsAsync(scenario);
        var relationship = scenario.AddRelationship('e', statement, action);
        scenario.AddStatement('c', revisionNumber: 2, correctionNote: PublicationScenario.PersonNote);

        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k2", Reference(PublishedRecordKind.Statement, statement),
                Reference(PublishedRecordKind.Relationship, relationship))));

        Assert.Equal(1, scenario.Releases.BeginCount);
    }

    [Fact]
    public async Task ARelationshipPublishesWithTheCitedRecordsThatTheSamePublishAdds()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('c');
        var action = scenario.AddAction('d');
        var relationship = scenario.AddRelationship('e', statement, action);

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            Reference(PublishedRecordKind.Relationship, relationship), Reference(PublishedRecordKind.Statement, statement),
            Reference(PublishedRecordKind.Action, action)));

        Assert.Equal(3, scenario.Publications.LastCommit!.Records.Length);
    }

    [Fact]
    public async Task ARelationshipThatIsNotApprovedIsRefusedBecauseNoModelOrDraftMaySpeakForTheSite()
    {
        var scenario = new PublicationScenario();
        var (statement, action, _) = await PublishRecordsAsync(scenario);
        var relationship = scenario.AddRelationship('e', statement, action, decision: ReviewDecisionKind.RequestChanges);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", relationship)));

        Assert.Equal($"Relationship {relationship} is not approved or has unresolved concerns.", error.Message);
    }

    [Fact]
    public async Task APublishThatAddsALinkKeepsTheRecordFileOfEachCitedRecordAsTheSameFile()
    {
        var scenario = new PublicationScenario();
        var (statement, action, other) = await PublishRecordsAsync(scenario);
        var first = scenario.Publications.Committed.Single().DirectoryName;
        var statementFile = scenario.Releases.Directories[first][PublicationProtocol.RecordDataPath(statement)];
        var relationship = scenario.AddRelationship('e', statement, action);
        scenario.Releases.Links.Clear();

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", relationship));

        var second = scenario.Releases.Directories[summary.DirectoryName];
        Assert.Same(statementFile, second[PublicationProtocol.RecordDataPath(statement)]);
        Assert.Contains((first, PublicationProtocol.RecordDataPath(statement)), scenario.Releases.Links);
        Assert.Contains((first, PublicationProtocol.RecordDataPath(action)), scenario.Releases.Links);
        Assert.Contains((first, PublicationProtocol.RecordDataPath(other)), scenario.Releases.Links);
    }

    [Fact]
    public async Task AReplacedRecordKeepsTheLinkFilesThatAnEarlierReleaseCarriesAsTheyAre()
    {
        var scenario = new PublicationScenario();
        var (statement, action, _) = await PublishRecordsAsync(scenario);
        var relationship = scenario.AddRelationship('e', statement, action);
        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", relationship));
        scenario.AddStatement('c', revisionNumber: 2, statementDate: StatementDate, correctionNote: PublicationScenario.PersonNote);
        scenario.Releases.Links.Clear();

        var third = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k3", Reference(PublishedRecordKind.Statement, statement)));

        Assert.Contains((second.DirectoryName, PublicationProtocol.RecordDataPath(relationship)), scenario.Releases.Links);
        Assert.Contains(new PublishedRevisionBinding(PublishedRecordKind.Relationship, relationship, 1), scenario.Publications.LastCommit!.Records);
    }

    [Fact]
    public async Task AReplacedLinkTakesTheNewRevisionInItsRecordFile()
    {
        var scenario = new PublicationScenario();
        var (statement, action, _) = await PublishRecordsAsync(scenario);
        var relationship = scenario.AddRelationship('e', statement, action);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", relationship));
        scenario.AddRelationship('e', statement, action, revisionNumber: 2, correctionNote: PublicationScenario.PersonNote);

        var third = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            Request("k3", relationship));

        Assert.Equal(2, ReadRecord<PublishedRelationship>(scenario, third.DirectoryName, relationship).RevisionNumber);
    }

    [Fact]
    public async Task APublishThatTouchesNoRecordThatListsRelatedRecordsReadsNoLinkFile()
    {
        var scenario = new PublicationScenario();
        var (statement, action, _) = await PublishRecordsAsync(scenario);
        var relationship = scenario.AddRelationship('e', statement, action);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", relationship));
        var draft = scenario.AddDraft('1');
        scenario.Releases.ReadPaths.Clear();

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k3", draft));

        Assert.Equal([PublicationProtocol.ManifestPath, PublicationProtocol.OfficialsPath, PublicationProtocol.OfficialSummariesPath, PublicationProtocol.BillStatusPath], scenario.Releases.ReadPaths);
    }

    [Fact]
    public async Task ARelationshipWhoseQuoteLiesOutsideEveryPassageOfItsStatementIsRefusedBecauseThePageWouldQuoteTextThatNoOneApproved()
    {
        var scenario = new PublicationScenario();
        var (statement, action, _) = await PublishRecordsAsync(scenario);
        var extractionId = scenario.Statements.Publishable[statement].Extraction.ExtractionId;
        var outside = scenario.AddRelationship('e', statement, action, quoteSpan: new(extractionId, 0, 5));
        var otherExtraction = scenario.AddRelationship('f', statement, action, quoteSpan: new(new string('9', 64), 12, 15));

        foreach (var relationship in new[] { outside, otherExtraction })
            await Assert.ThrowsAsync<InvalidDataException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k" + relationship[0], relationship)));
    }

    private static async Task<(string Statement, string Action, string Other)> PublishRecordsAsync(PublicationScenario scenario)
    {
        var statement = scenario.AddStatement('c', statementDate: StatementDate);
        var action = scenario.AddAction('d');
        var other = scenario.AddStatement('b');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            Reference(PublishedRecordKind.Statement, statement), Reference(PublishedRecordKind.Action, action),
            Reference(PublishedRecordKind.Statement, other)));
        return (statement, action, other);
    }

    private static PublishedRecordReference Reference(PublishedRecordKind kind, string id) => new(kind, id);

    private static PublishReleaseRequest Request(string key, string relationshipId) =>
        PublicationScenario.Request(key, Reference(PublishedRecordKind.Relationship, relationshipId));

    private static TRecord ReadRecord<TRecord>(PublicationScenario scenario, string directory, string recordId) =>
        JsonSerializer.Deserialize<TRecord>(scenario.Releases.Directories[directory][PublicationProtocol.RecordDataPath(recordId)],
            PublicationProtocol.JsonOptions)!;

    private static PublicationRelease ReadManifest(PublicationScenario scenario, string directory) =>
        JsonSerializer.Deserialize<PublicationRelease>(
            scenario.Releases.Directories[directory][PublicationProtocol.ManifestPath], PublicationProtocol.JsonOptions)!;
}
