using System.Text;
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
    public async Task TheOfficialPageListsEachLinkOfAStatementOfTheOfficialWithTheBillTitleAndTheCastOfTheOfficial()
    {
        var scenario = new PublicationScenario();
        var (statement, action, _) = await PublishRecordsAsync(scenario);
        var vote = scenario.AddVote('a');
        var toBill = scenario.AddRelationship('e', statement, action);
        var toVote = scenario.AddRelationship('f', statement, vote, PublishedRecordKind.Vote);

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2",
            Reference(PublishedRecordKind.Vote, vote), Reference(PublishedRecordKind.Relationship, toBill), Reference(PublishedRecordKind.Relationship, toVote)));

        var pairs = scenario.OfficialPages.Last(page => page.Profile.Id == "mayor" && page.IsCurrent).Pairs;
        Assert.Equal(2, pairs.Length);
        var bill = pairs.Single(pair => pair.TargetRecordId == action);
        Assert.Equal(("A housing bill", null), (bill.TargetTitle, bill.Cast));
        Assert.Equal("Yea", pairs.Single(pair => pair.TargetRecordId == vote).Cast);
        Assert.All(pairs, pair => Assert.Equal((statement, "Statement c", StatementDate), (pair.StatementRecordId, pair.StatementHeadline, pair.StatementDay)));
    }

    [Fact]
    public async Task ABillThatTheOfficialDoesNotSponsorStillTouchesTheOfficialPageWhenItIsReplacedBecauseThePairShowsItsTitle()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('c', statementDate: StatementDate);
        var action = scenario.AddAction('d', sponsorId: "chair");
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            Reference(PublishedRecordKind.Statement, statement), Reference(PublishedRecordKind.Action, action)));
        var relationship = scenario.AddRelationship('e', statement, action);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", relationship));
        scenario.Releases.WriteOrder.Clear();
        scenario.ReviseAction(action, 2);

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k3", Reference(PublishedRecordKind.Action, action)));

        Assert.Contains(PublicationProtocol.OfficialPagePath("mayor"), scenario.Releases.WriteOrder);
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
    public async Task ARelationshipPublishesWithTheCitedRecordsThatTheSamePublishAddsAndTheirPagesListIt()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('c');
        var action = scenario.AddAction('d');
        var relationship = scenario.AddRelationship('e', statement, action);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            Reference(PublishedRecordKind.Relationship, relationship), Reference(PublishedRecordKind.Statement, statement),
            Reference(PublishedRecordKind.Action, action)));

        Assert.Equal(3, scenario.Publications.LastCommit!.Records.Length);
        Assert.Equal($"<html>statement {statement} related e</html>", PageOf(scenario, summary.DirectoryName, statement));
        Assert.Equal($"<html>action {action} related e</html>", PageOf(scenario, summary.DirectoryName, action));
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
    public async Task APublishThatAddsALinkWritesAgainThePageOfEachCitedRecordAndKeepsItsRecordFileAsTheSameFile()
    {
        var scenario = new PublicationScenario();
        var (statement, action, other) = await PublishRecordsAsync(scenario);
        var first = scenario.Publications.Committed.Single().DirectoryName;
        var statementFile = scenario.Releases.Directories[first][PublicationProtocol.RecordDataPath(statement)];
        var relationship = scenario.AddRelationship('e', statement, action);
        scenario.Releases.Links.Clear();

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", relationship));

        var second = scenario.Releases.Directories[summary.DirectoryName];
        Assert.Equal($"<html>statement {statement} related e</html>", PageOf(scenario, summary.DirectoryName, statement));
        Assert.Equal($"<html>action {action} related e</html>", PageOf(scenario, summary.DirectoryName, action));
        Assert.Same(statementFile, second[PublicationProtocol.RecordDataPath(statement)]);
        Assert.Contains((first, PublicationProtocol.RecordDataPath(statement)), scenario.Releases.Links);
        Assert.Contains((first, PublicationProtocol.RecordDataPath(action)), scenario.Releases.Links);
        Assert.DoesNotContain((first, PublicationProtocol.RecordPagePath(statement)), scenario.Releases.Links);
        Assert.DoesNotContain((first, PublicationProtocol.RecordPagePath(action)), scenario.Releases.Links);
        Assert.Contains((first, PublicationProtocol.RecordPagePath(other)), scenario.Releases.Links);
        Assert.Contains((first, PublicationProtocol.RecordDataPath(other)), scenario.Releases.Links);
    }

    [Fact]
    public async Task AReplacedRecordPageListsTheLinksThatAnEarlierReleaseCarriesAndTheLinkFilesAreCarriedAsTheyAre()
    {
        var scenario = new PublicationScenario();
        var (statement, action, _) = await PublishRecordsAsync(scenario);
        var relationship = scenario.AddRelationship('e', statement, action);
        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", relationship));
        scenario.AddStatement('c', revisionNumber: 2, statementDate: StatementDate, correctionNote: PublicationScenario.PersonNote);
        scenario.Releases.Links.Clear();

        var third = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k3", Reference(PublishedRecordKind.Statement, statement)));

        Assert.Equal($"<html>statement {statement} related e</html>", PageOf(scenario, third.DirectoryName, statement));
        Assert.Contains((second.DirectoryName, PublicationProtocol.RecordDataPath(relationship)), scenario.Releases.Links);
        Assert.Contains((second.DirectoryName, PublicationProtocol.RecordPagePath(relationship)), scenario.Releases.Links);
        Assert.Contains((second.DirectoryName, PublicationProtocol.RecordPagePath(action)), scenario.Releases.Links);
        Assert.Contains(new PublishedRevisionBinding(PublishedRecordKind.Relationship, relationship, 1), scenario.Publications.LastCommit!.Records);
    }

    [Fact]
    public async Task AReplacedLinkIsListedOnceBecauseTheNewRevisionTakesThePlaceOfTheCarriedOne()
    {
        var scenario = new PublicationScenario();
        var (statement, action, _) = await PublishRecordsAsync(scenario);
        var relationship = scenario.AddRelationship('e', statement, action);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", relationship));
        scenario.AddRelationship('e', statement, action, revisionNumber: 2, correctionNote: PublicationScenario.PersonNote);

        var third = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            Request("k3", relationship));

        Assert.Equal($"<html>statement {statement} related e</html>", PageOf(scenario, third.DirectoryName, statement));
        Assert.Equal(2, ReadRecord<PublishedRelationship>(scenario, third.DirectoryName, relationship).RevisionNumber);
    }

    [Fact]
    public async Task TheRelatedListFollowsTheStatementDateAndPutsAnUndatedStatementLast()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('d');
        var march = scenario.AddStatement('a', statementDate: new DateOnly(2026, 3, 1));
        var january = scenario.AddStatement('b', statementDate: new DateOnly(2026, 1, 1));
        var undated = scenario.AddStatement('c');
        var links = new[]
        {
            scenario.AddRelationship('1', march, action), scenario.AddRelationship('2', january, action), scenario.AddRelationship('3', undated, action)
        };

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", [
            Reference(PublishedRecordKind.Action, action), Reference(PublishedRecordKind.Statement, march),
            Reference(PublishedRecordKind.Statement, january), Reference(PublishedRecordKind.Statement, undated),
            .. links.Select(link => Reference(PublishedRecordKind.Relationship, link))]));

        Assert.Equal($"<html>action {action} related 2 1 3</html>", PageOf(scenario, summary.DirectoryName, action));
    }

    [Fact]
    public async Task ACarriedVoteKeepsTheLinkToItsBillActionWhenALinkMakesItsPageRenderAgain()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('c');
        var action = scenario.AddAction('d');
        var vote = scenario.AddVote('f', billActionRecordIds: [action]);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            Reference(PublishedRecordKind.Statement, statement), Reference(PublishedRecordKind.Action, action),
            Reference(PublishedRecordKind.Vote, vote)));
        var relationship = scenario.AddRelationship('e', statement, vote, PublishedRecordKind.Vote);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", relationship));

        Assert.Equal($"<html>vote {vote} bill {action} related e</html>", PageOf(scenario, summary.DirectoryName, vote));
        Assert.Equal(1, scenario.Votes.BillLookups);
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
    public async Task OnlyTheCarriedLinksThatCiteAnAddedRecordAreReadFromDiskSoThePublishCostFollowsTheLinksOfThatRecord()
    {
        var scenario = new PublicationScenario();
        var (statement, action, _) = await PublishRecordsAsync(scenario);
        var otherStatement = scenario.AddStatement('a');
        var otherAction = scenario.AddAction('f');
        var citing = scenario.AddRelationship('e', statement, action);
        var unrelated = scenario.AddRelationship('1', otherStatement, otherAction);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2",
            Reference(PublishedRecordKind.Statement, otherStatement), Reference(PublishedRecordKind.Action, otherAction),
            Reference(PublishedRecordKind.Relationship, citing), Reference(PublishedRecordKind.Relationship, unrelated)));
        scenario.AddStatement('c', revisionNumber: 2, statementDate: StatementDate, correctionNote: PublicationScenario.PersonNote);
        scenario.Releases.ReadPaths.Clear();

        var third = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k3", Reference(PublishedRecordKind.Statement, statement)));

        Assert.Equal([PublicationProtocol.ManifestPath, PublicationProtocol.OfficialsPath, PublicationProtocol.OfficialSummariesPath, PublicationProtocol.BillStatusPath, PublicationProtocol.RecordDataPath(statement), PublicationProtocol.RecordDataPath(citing)], scenario.Releases.ReadPaths);
        Assert.Equal($"<html>statement {statement} related e</html>", PageOf(scenario, third.DirectoryName, statement));
    }

    [Fact]
    public async Task EachCarriedRecordIsReadAndRenderedBeforeTheNextIsReadSoPeakMemoryStaysAtAboutOneRecord()
    {
        var scenario = new PublicationScenario();
        var (statement, action, _) = await PublishRecordsAsync(scenario);
        var relationship = scenario.AddRelationship('e', statement, action);
        scenario.Events.Clear();

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", relationship));

        var carried = scenario.Events.Where(entry => entry is "read c" or "read d" or "render c" or "render d").ToArray();
        Assert.Equal(["read", "render", "read", "render"], carried.Select(entry => entry.Split(' ')[0]));
        Assert.Equal(carried[0].Split(' ')[1], carried[1].Split(' ')[1]);
        Assert.Equal(carried[2].Split(' ')[1], carried[3].Split(' ')[1]);
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

    [Fact]
    public async Task AJointStatementPutsItsPairOnTheMatchedOfficialAndAReplacedBillTouchesOnlyThatOfficial()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('c', statementDate: StatementDate, speakerIds: ["chair", "mayor"]);
        var action = scenario.AddAction('d', sponsorId: "chair");
        var vote = scenario.AddVote('a', memberIds: ["chair"]);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            Reference(PublishedRecordKind.Statement, statement), Reference(PublishedRecordKind.Action, action), Reference(PublishedRecordKind.Vote, vote)));
        var relationship = scenario.AddRelationship('e', statement, action);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", relationship));
        Assert.Single(scenario.OfficialPages.Last(page => page.Profile.Id == "chair" && page.IsCurrent).Pairs);
        Assert.Empty(scenario.OfficialPages.Last(page => page.Profile.Id == "mayor" && page.IsCurrent).Pairs);
        scenario.Releases.WriteOrder.Clear();
        scenario.ReviseAction(action, 2);

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k3", Reference(PublishedRecordKind.Action, action)));

        Assert.Contains(PublicationProtocol.OfficialPagePath("chair"), scenario.Releases.WriteOrder);
        Assert.DoesNotContain(PublicationProtocol.OfficialPagePath("mayor"), scenario.Releases.WriteOrder);
    }

    [Fact]
    public async Task AVoteThatIsReplacedTouchesTheOfficialPageOfASpeakerWhoIsNotAMemberOfItsChamber()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('c', statementDate: StatementDate);
        var vote = scenario.AddVote('a', memberIds: ["chair"]);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            Reference(PublishedRecordKind.Statement, statement), Reference(PublishedRecordKind.Vote, vote)));
        var relationship = scenario.AddRelationship('e', statement, vote, PublishedRecordKind.Vote);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", relationship));
        scenario.Releases.WriteOrder.Clear();
        scenario.ReviseVote(vote, 2);

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k3", Reference(PublishedRecordKind.Vote, vote)));

        Assert.Contains(PublicationProtocol.OfficialPagePath("mayor"), scenario.Releases.WriteOrder);
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

    private static string PageOf(PublicationScenario scenario, string directory, string recordId) =>
        Encoding.UTF8.GetString(scenario.Releases.Directories[directory][PublicationProtocol.RecordPagePath(recordId)]);

    private static TRecord ReadRecord<TRecord>(PublicationScenario scenario, string directory, string recordId) =>
        JsonSerializer.Deserialize<TRecord>(scenario.Releases.Directories[directory][PublicationProtocol.RecordDataPath(recordId)],
            PublicationProtocol.JsonOptions)!;

    private static PublicationRelease ReadManifest(PublicationScenario scenario, string directory) =>
        JsonSerializer.Deserialize<PublicationRelease>(
            scenario.Releases.Directories[directory][PublicationProtocol.ManifestPath], PublicationProtocol.JsonOptions)!;
}
