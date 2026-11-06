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

    [Theory]
    [InlineData("This bill funds rent aid for veterans. It also sets a cap on fees.", "This bill funds rent aid for veterans.")]
    [InlineData("This bill amends 5 U.S.C. to fund aid. It adds a cap.", "This bill amends 5 U.S.C. to fund aid.")]
    [InlineData("This bill requires the U.S. Department of Agriculture to fund aid. It adds a cap.",
        "This bill requires the U.S. Department of Agriculture to fund aid.")]
    [InlineData("The D.C. Council may act. It adds a cap.", "The D.C. Council may act.")]
    [InlineData("This bill names H.R. Smith as chair. It adds a cap.", "This bill names H.R. Smith as chair.")]
    [InlineData("This bill moves the office to St. Louis. It adds a cap.", "This bill moves the office to St. Louis.")]
    [InlineData("Dr. Smith and Mr. Jones lead the board. It adds a cap.", "Dr. Smith and Mr. Jones lead the board.")]
    [InlineData("This bill amends Pub. L. 117-58 to fund aid. It adds a cap.", "This bill amends Pub. L. 117-58 to fund aid.")]
    [InlineData("This bill funds aid. \"It adds a cap.\" It adds more.", "This bill funds aid.")]
    [InlineData("This bill funds aid. (It adds a cap.) It adds more.", "This bill funds aid.")]
    [InlineData("This bill funds aid? It adds a cap.", "This bill funds aid?")]
    [InlineData("Veterans Rent Aid Act\nThis bill funds rent aid.", "This bill funds rent aid.")]
    [InlineData("This bill names the \"John Doe Post Office.\"", "This bill names the \"John Doe Post Office.\"")]
    [InlineData("This bill names the \"John Doe Post Office.\" It adds a cap.", "This bill names the \"John Doe Post Office.\"")]
    [InlineData("This bill names the “John Doe Post Office.”", "This bill names the “John Doe Post Office.”")]
    [InlineData("This bill names the “John Doe Post Office.” It adds a cap.", "This bill names the “John Doe Post Office.”")]
    [InlineData("This bill funds aid (as defined in law.)", "This bill funds aid (as defined in law.)")]
    [InlineData("This bill funds aid (as defined in law.) It adds a cap.", "This bill funds aid (as defined in law.)")]
    [InlineData("Rent Relief Act (RRA)\nThis bill funds rent aid.", "This bill funds rent aid.")]
    [InlineData("This bill honors Mr. Jones. It adds a cap.", "This bill honors Mr. Jones.")]
    [InlineData("This bill honors Mrs. Jones. It adds a cap.", "This bill honors Mrs. Jones.")]
    [InlineData("This bill honors Ms. Jones. It adds a cap.", "This bill honors Ms. Jones.")]
    [InlineData("Rep. Jane Doe sponsors this bill. It adds a cap.", "Rep. Jane Doe sponsors this bill.")]
    [InlineData("Sen. Jane Doe sponsors this bill. It adds a cap.", "Sen. Jane Doe sponsors this bill.")]
    [InlineData("This bill amends Sec. A to fund aid. It adds a cap.", "This bill amends Sec. A to fund aid.")]
    [InlineData("This bill amends No. A to fund aid. It adds a cap.", "This bill amends No. A to fund aid.")]
    [InlineData("This bill amends Pub. A to fund aid. It adds a cap.", "This bill amends Pub. A to fund aid.")]
    [InlineData("This bill honors Smith, e.g. Jones. It adds a cap.", "This bill honors Smith, e.g. Jones.")]
    public async Task TheEntryLineIsTheFirstSentenceOfTheCrsSummaryBecauseOneRowNeedsOneShortLine(string summaryText, string expected)
    {
        Assert.Equal(expected, await PublishedLineAsync(summaryText));
    }

    [Theory]
    [InlineData("This bill:\nAmends tax law.\nFunds aid.")]
    [InlineData("Veterans Rent Aid Act\nThis bill:\nAmends tax law.")]
    [InlineData("")]
    [InlineData("Veterans Rent Aid Act")]
    public async Task ASummaryWithNoSentenceOrAListIntroHasNoLineBecauseAListItemIsNotTheSummary(string summaryText)
    {
        Assert.Null(await PublishedLineAsync(summaryText));
    }

    [Fact]
    public async Task ALongFirstSentenceIsCutAtAWordBoundaryAndEndsWithTheCutMarkerWithinTheCap()
    {
        var line = await PublishedLineAsync(string.Join(' ', Enumerable.Repeat("grants", 60)) + ".");

        Assert.Equal(string.Join(' ', Enumerable.Repeat("grants", 28)) + "…", line);
        Assert.True(line!.Length <= PublishedRecordEntry.MaximumBillLineLength);
    }

    [Theory]
    [InlineData(",")]
    [InlineData(";")]
    [InlineData(":")]
    public async Task ACutDropsATrailingCommaSemicolonOrColonBeforeTheCutMarker(string mark)
    {
        var text = string.Join(' ', Enumerable.Repeat("grants", 27)) + " aid" + mark + " grants grants.";

        Assert.Equal(string.Join(' ', Enumerable.Repeat("grants", 27)) + " aid…", await PublishedLineAsync(text));
    }

    [Fact]
    public async Task ANonBreakingSpaceIsAWordBoundaryForTheCut()
    {
        var text = string.Join('\u00A0', Enumerable.Repeat("grants", 60)) + ".";

        Assert.Equal(string.Join('\u00A0', Enumerable.Repeat("grants", 28)) + "…", await PublishedLineAsync(text));
    }

    [Fact]
    public async Task ASentenceWithNoWhitespaceInsideTheCapHasNoLine()
    {
        Assert.Null(await PublishedLineAsync(new string('x', PublishedRecordEntry.MaximumBillLineLength + 50) + ". It adds a cap."));
    }

    [Fact]
    public async Task AnActionWithNoCrsSummaryHasNoLineAndNeverShowsTheActionTextInItsPlace()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", action));

        var record = JsonSerializer.Deserialize<PublishedAction>(
            scenario.Releases.Directories[summary.DirectoryName][PublicationProtocol.RecordDataPath(action)], PublicationProtocol.JsonOptions)!;
        Assert.NotEmpty(record.Summary);
        Assert.Null(Assert.Single(ReadManifest(scenario, summary.DirectoryName).Records).BillLine);
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
        scenario.ReviseAction(action, 2, correctionNote: "Fixed the tracked issue.");
        scenario.Clock.Now = firstPublishedAt.AddDays(1);

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action));

        var entry = Assert.Single(ReadManifest(scenario, second.DirectoryName).Records);
        Assert.Equal(2, entry.RevisionNumber);
        Assert.Equal(["issue-2"], entry.IssueIds);
        Assert.Equal(firstPublishedAt, entry.FirstPublishedAtUtc);
        Assert.Empty(scenario.Releases.Links);
    }

    [Fact]
    public async Task AnApprovedTextDraftPublishesItsSentencesWithTheExactQuotesAndTheVersionItCitesSoThePublishedActionCitesItsTextSource()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        scenario.DraftActionFromBillText(action);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", action));

        var files = scenario.Releases.Directories[summary.DirectoryName];
        var record = JsonSerializer.Deserialize<PublishedAction>(files[PublicationProtocol.RecordDataPath(action)], PublicationProtocol.JsonOptions)!;
        var text = record.BillText!;
        Assert.Equal(["One.", "Two.", "Three."], text.Sentences.Select(item => item.Sentence));
        Assert.Equal([PublishedTextPart.Summary, PublishedTextPart.Summary, PublishedTextPart.WhyItMatters], text.Sentences.Select(item => item.Part));
        Assert.All(text.Sentences, item => Assert.Equal("The program ends", item.Citation.Quote));
        Assert.Equal(("Introduced in House", new DateOnly(2026, 1, 3)), (text.VersionName, text.VersionDate));
        var source = Assert.Single(record.Sources, item => item.ExtractionId == text.Sentences[0].Citation.ExtractionId);
        Assert.Contains("The program ends on December 31, 2030.", source.Text, StringComparison.Ordinal);
        Assert.Null(Assert.Single(ReadManifest(scenario, summary.DirectoryName).Records).BillLine);
    }

    [Fact]
    public async Task ATextDraftWithoutItsStoredVersionRowStopsThePublishBecauseThePageCouldNotNameTheVersionItCites()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        scenario.DraftActionFromBillText(action);
        scenario.Actions.Publishable[action] = scenario.Actions.Publishable[action] with { TextVersion = null };

        await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", action)));

        Assert.Empty(scenario.Publications.Committed);
    }

    [Fact]
    public async Task ATextDraftWithTheRowOfAnotherVersionStopsThePublishBecausePublicationNamesTheExactVersionTheRevisionCites()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        scenario.DraftActionFromBillText(action);
        var publishable = scenario.Actions.Publishable[action];
        scenario.Actions.Publishable[action] = publishable with { TextVersion = publishable.TextVersion! with { PackageId = "BILLS-119hr7eh" } };

        await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", action)));
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

    private static async Task<string?> PublishedLineAsync(string summaryText)
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c', summaryText: summaryText);
        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", action));
        return Assert.Single(ReadManifest(scenario, summary.DirectoryName).Records).BillLine;
    }

    private static PublicationRelease ReadManifest(PublicationScenario scenario, string directory) =>
        JsonSerializer.Deserialize<PublicationRelease>(
            scenario.Releases.Directories[directory][PublicationProtocol.ManifestPath], PublicationProtocol.JsonOptions)!;
}
