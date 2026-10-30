using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishStatementTests
{
    [Fact]
    public async Task OneReleaseHoldsAStatementAndADocumentChangeEachUnderItsOwnKind()
    {
        var scenario = new PublicationScenario();
        var change = scenario.AddDraft('a');
        var statement = scenario.AddStatement('b', speakerIds: ["mayor"]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Statement, statement),
            new PublishedRecordReference(PublishedRecordKind.DocumentChange, change)));

        var manifest = ReadManifest(scenario, summary.DirectoryName);
        Assert.Equal(PublicationProtocol.SchemaVersion, manifest.SchemaVersion);
        Assert.Equal([PublishedRecordKind.DocumentChange, PublishedRecordKind.Statement],
            manifest.Records.OrderBy(entry => entry.RecordId.Length).Select(entry => entry.Kind));
        var files = scenario.Releases.Directories[summary.DirectoryName];
        Assert.Contains(PublicationProtocol.RecordDataPath(change), files.Keys);
        Assert.Contains(PublicationProtocol.RecordPagePath(change), files.Keys);
        Assert.Contains(PublicationProtocol.RecordDataPath(statement), files.Keys);
        Assert.Contains(PublicationProtocol.RecordPagePath(statement), files.Keys);
        var record = JsonSerializer.Deserialize<PublishedStatement>(files[PublicationProtocol.RecordDataPath(statement)],
            PublicationProtocol.JsonOptions)!;
        Assert.Equal("housing matters", Assert.Single(record.Quotations).Quote);
        Assert.Equal(PublishedQuotationKind.DirectQuotation, record.Quotations[0].Kind);
        Assert.Equal("Mayor Example", Assert.Single(record.Speakers).Name);
        Assert.Equal(PublicationScenario.ApprovedAt, record.ApprovedAtUtc);
        Assert.Equal(scenario.Clock.Now, record.FirstPublishedAtUtc);
    }

    [Fact]
    public async Task ACodeApprovedAnalysisStatementPublishesAsAiTextCheckedByCodeAndAPersonApprovedOnePublishesAsPersonApproved()
    {
        var scenario = new PublicationScenario();
        var codeDecision = PublicationScenario.Decision(new string('b', 64), 1, ReviewDecisionKind.Approve) with { ActorSubject = ReviewAuthor.CodeSubject };
        var byCode = scenario.AddStatement('b', [codeDecision], author: ReviewAuthor.AnalysisSubject);
        var byPerson = scenario.AddStatement('c');

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Statement, byCode), new PublishedRecordReference(PublishedRecordKind.Statement, byPerson)));

        PublishedStatement Read(string id) => JsonSerializer.Deserialize<PublishedStatement>(
            scenario.Releases.Directories[summary.DirectoryName][PublicationProtocol.RecordDataPath(id)], PublicationProtocol.JsonOptions)!;
        Assert.Equal((PublishedTextOrigin.Model, PublishedApprover.Code), (Read(byCode).TextOrigin, Read(byCode).ApprovedBy));
        Assert.Equal((PublishedTextOrigin.Person, PublishedApprover.Person), (Read(byPerson).TextOrigin, Read(byPerson).ApprovedBy));
    }

    [Fact]
    public async Task AStatementFromAFeedCitesTheFeedCaptureAndLinksTheArticleBecauseTheReaderNeedsBoth()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('b', feedUrl: "https://example.test/feed");

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Statement, statement)));

        var record = JsonSerializer.Deserialize<PublishedStatement>(
            scenario.Releases.Directories[summary.DirectoryName][PublicationProtocol.RecordDataPath(statement)], PublicationProtocol.JsonOptions)!;
        Assert.Equal("https://example.test/feed", record.Source.Url);
        Assert.Equal(PublicationScenario.StatementArticleUrl, record.ArticleUrl);
    }

    [Fact]
    public async Task AStatementFromTheArticlePageHasNoSeparateArticleLinkBecauseItsSourceIsTheArticle()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('b');

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Statement, statement)));

        var record = JsonSerializer.Deserialize<PublishedStatement>(
            scenario.Releases.Directories[summary.DirectoryName][PublicationProtocol.RecordDataPath(statement)], PublicationProtocol.JsonOptions)!;
        Assert.Equal(PublicationScenario.StatementArticleUrl, record.Source.Url);
        Assert.Null(record.ArticleUrl);
    }

    [Fact]
    public async Task EachKindFillsItsSearchFiltersFromTheRevisionItPublishes()
    {
        var scenario = new PublicationScenario();
        var change = scenario.AddDraft('a', officialIds: ["mayor"]);
        var changeItem = scenario.Reviews.Items[change];
        var changeRevision = changeItem.Review.CurrentRevision with
        {
            ChangeDate = new DateOnly(2026, 1, 5),
            ChangeDateEvidence = new(changeItem.After.ExtractionId, 6, 5)
        };
        scenario.Reviews.Items[change] = changeItem with
        {
            Review = changeItem.Review with { CurrentRevision = changeRevision, Revisions = [changeRevision] }
        };
        var statement = scenario.AddStatement('b');
        var statementItem = scenario.Statements.Publishable[statement];
        var statementRevision = statementItem.Review.CurrentRevision! with
        {
            StatementDate = new DateOnly(2026, 1, 7),
            StatementDateEvidence = new(statementItem.Extraction.ExtractionId, 0, 5)
        };
        scenario.Statements.Publishable[statement] = statementItem with
        {
            Review = statementItem.Review with { CurrentRevision = statementRevision, Revisions = [statementRevision] }
        };

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Statement, statement),
            new PublishedRecordReference(PublishedRecordKind.DocumentChange, change)));

        var records = ReadManifest(scenario, summary.DirectoryName).Records;
        var changeEntry = records.Single(entry => entry.Kind == PublishedRecordKind.DocumentChange);
        Assert.Equal(("mayor", "Mayor Example"), (Assert.Single(changeEntry.Officials).Id, changeEntry.Officials[0].Name));
        Assert.Equal(["housing"], changeEntry.IssueIds);
        Assert.Empty(changeEntry.PolicyAreaIds);
        Assert.Equal("example.test", changeEntry.SourceHost);
        Assert.Equal(new DateOnly(2026, 1, 5), changeEntry.EventDate);
        var statementEntry = records.Single(entry => entry.Kind == PublishedRecordKind.Statement);
        Assert.Equal(("mayor", "Mayor Example"), (Assert.Single(statementEntry.Officials).Id, statementEntry.Officials[0].Name));
        Assert.Equal(["housing"], statementEntry.IssueIds);
        Assert.Equal(["land-use"], statementEntry.PolicyAreaIds);
        Assert.Equal("example.test", statementEntry.SourceHost);
        Assert.Equal(new DateOnly(2026, 1, 7), statementEntry.EventDate);
    }

    [Fact]
    public async Task ARecordWithoutAnEvidencedDateHasNoEventDateRatherThanAGuessedOne()
    {
        var scenario = new PublicationScenario();
        var change = scenario.AddDraft('a');

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", change));

        var entry = Assert.Single(ReadManifest(scenario, summary.DirectoryName).Records);
        Assert.Null(entry.EventDate);
        Assert.Empty(entry.Officials);
    }

    [Fact]
    public async Task ARequestWithOnlyDocumentChangesNeverAsksTheStatementStoreBecauseItHasNothingToLoad()
    {
        var scenario = new PublicationScenario();

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", scenario.AddDraft('a')));

        Assert.Equal(0, scenario.Statements.PublicationReadCount);
    }

    [Fact]
    public async Task ARequestWithOnlyStatementsNeverAsksTheDocumentChangeStore()
    {
        var scenario = new PublicationScenario();

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('b'))));

        Assert.Equal(0, scenario.Reviews.ReadCount);
    }

    [Fact]
    public async Task CommitBindsEachRecordToItsKindAndReviewStateSoTheStoreChecksTheRightDraftTable()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('b');

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Statement, statement)));

        var commit = scenario.Publications.LastCommit!;
        Assert.Equal(new PublishedRevisionBinding(PublishedRecordKind.Statement, statement, 1), Assert.Single(commit.Records));
        Assert.Equal(new ReviewStateExpectation(PublishedRecordKind.Statement, statement, 1, 1), Assert.Single(commit.AddedRecords));
    }

    [Fact]
    public async Task StatementAlreadyPublishedAtItsCurrentRevisionIsRejectedSoReleasesDoNotRepeatIt()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('b');
        var reference = new PublishedRecordReference(PublishedRecordKind.Statement, statement);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", reference));

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", reference)));

        Assert.Contains("already published at its current revision", error.Message, StringComparison.Ordinal);
        Assert.Single(scenario.Publications.Committed);
    }

    [Fact]
    public async Task ADocumentChangeWithTheSameIdDoesNotCountAsThePublishedStatement()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('b');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Statement, statement)));

        var change = scenario.AddDraft('a');
        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", change));

        Assert.Equal(2, ReadManifest(scenario, summary.DirectoryName).Records.Length);
    }

    [Fact]
    public async Task CarriedStatementIsHardLinkedFromThePreviousReleaseWithoutRebuildingIt()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('b');
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Statement, statement)));
        scenario.Releases.Links.Clear();
        scenario.Statements.Publishable.Clear();

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k2", scenario.AddDraft('a')));

        Assert.Equal([(first.DirectoryName, PublicationProtocol.RecordDataPath(statement)),
            (first.DirectoryName, PublicationProtocol.RecordPagePath(statement))],
            scenario.Releases.Links.Where(link => link.RelativePath.StartsWith("records/", StringComparison.Ordinal)));
        var carried = Assert.Single(ReadManifest(scenario, second.DirectoryName).Records, entry => entry.Kind == PublishedRecordKind.Statement);
        Assert.Equal(("mayor", "Mayor Example"), (Assert.Single(carried.Officials).Id, carried.Officials[0].Name));
        Assert.Equal(["housing"], carried.IssueIds);
    }

    [Fact]
    public async Task ARepublishedStatementReplacesItsCarriedEntryInsteadOfDuplicatingIt()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('b');
        var reference = new PublishedRecordReference(PublishedRecordKind.Statement, statement);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", reference));
        var review = scenario.Statements.Publishable[statement].Review;
        var revised = review.CurrentRevision! with { RevisionNumber = 2, IssueIds = ["transit"], PolicyAreaIds = ["transportation"] };
        var decisions = review.Decisions.Add(PublicationScenario.Decision(statement, 2, ReviewDecisionKind.Approve) with { RevisionNumber = 2 });
        scenario.Statements.Publishable[statement] = scenario.Statements.Publishable[statement] with
        {
            Review = review with
            {
                CurrentRevision = revised,
                Revisions = review.Revisions.Add(revised),
                Decisions = decisions,
                ReviewStateVersion = 2
            }
        };

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", reference) with { Notes = [new(PublishedRecordKind.Statement, statement, "Fixed the issue.")] });

        var entry = Assert.Single(ReadManifest(scenario, second.DirectoryName).Records);
        Assert.Equal(2, entry.RevisionNumber);
        Assert.Equal(["transit"], entry.IssueIds);
        Assert.Equal(["transportation"], entry.PolicyAreaIds);
        Assert.Empty(scenario.Releases.Links);
    }

    [Fact]
    public async Task StatementThatIsNotApprovedIsRefusedBecauseNoModelOrDraftMaySpeakForTheSite()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('b', decisions: [PublicationScenario.Decision(new string('b', 64), 1, ReviewDecisionKind.RequestChanges)]);

        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", new PublishedRecordReference(PublishedRecordKind.Statement, statement))));

        Assert.Equal(0, scenario.Releases.BeginCount);
    }

    [Fact]
    public async Task StatementSpeakerWithoutAPublicNameFailsBeforeAnythingIsStaged()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('b', speakerIds: ["unnamed"]);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", new PublishedRecordReference(PublishedRecordKind.Statement, statement))));

        Assert.Contains("has no configured public name", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, scenario.Releases.BeginCount);
    }

    [Theory]
    [InlineData(PublishedRecordKind.Statement, 32)]
    [InlineData(PublishedRecordKind.DocumentChange, 64)]
    [InlineData(PublishedRecordKind.Action, 32)]
    public async Task RecordIdMustMatchTheIdShapeOfItsKindSoAKindCannotBeMislabelled(PublishedRecordKind kind, int length)
    {
        var scenario = new PublicationScenario();

        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", new PublishedRecordReference(kind, new string('a', length)))));

        Assert.Equal((0, 0), (scenario.Statements.PublicationReadCount, scenario.Actions.PublicationReadCount));
    }

    [Fact]
    public async Task ReleaseCannotNameTheSameRecordTwice()
    {
        var scenario = new PublicationScenario();
        var reference = new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('b'));

        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", reference, reference)));
    }

    [Fact]
    public async Task AReleaseOfAnotherSchemaVersionCannotBeTheBaseOfTheNextRelease()
    {
        var scenario = new PublicationScenario();
        scenario.SeedReleaseOfAnotherSchemaVersion();

        await Assert.ThrowsAsync<InvalidDataException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", scenario.AddDraft('a'))));
    }

    private static PublicationRelease ReadManifest(PublicationScenario scenario, string directory) =>
        JsonSerializer.Deserialize<PublicationRelease>(
            scenario.Releases.Directories[directory][PublicationProtocol.ManifestPath], PublicationProtocol.JsonOptions)!;
}
