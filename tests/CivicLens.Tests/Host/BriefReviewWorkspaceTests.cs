using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.Core.Briefs;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using static CivicLens.Tests.Fixtures.BriefFixtures;
using static CivicLens.Tests.Fixtures.BriefWorldSeeder;
using static CivicLens.Tests.Host.ReviewWorkspaceHost;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class BriefReviewWorkspaceTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly ReviewWorkspaceHost host = new(postgres);
    private BriefWorldSeeder seeder = null!;

    public async Task InitializeAsync()
    {
        await host.StartAsync();
        seeder = new BriefWorldSeeder(host.ConnectionString);
    }

    public Task DisposeAsync() => host.StopAsync();

    [Fact]
    public async Task TheRailLinksTheBriefListWhichCountsEachFilterAndListsDraftedBriefsOnly()
    {
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var empty = await host.GetDocumentAsync("/Review/Briefs");

        Assert.Equal("page", empty.QuerySelector(".nav a[href='/Review/Briefs']")!.GetAttribute("aria-current"));
        Assert.Null(empty.QuerySelector(".nav a[href='/Review']")!.GetAttribute("aria-current"));
        Assert.Equal(["0", "0", "0"], empty.QuerySelectorAll("[aria-labelledby='drafts'] .segmented-count").Select(count => count.TextContent));
        Assert.Equal("No weekly briefs yet", empty.QuerySelector("[aria-labelledby='drafts'] .empty h3")!.TextContent);

        await seeder.QueueWeekAsync(new DateOnly(2026, 10, 19));
        await seeder.ReleaseCitedRecordsAsync();
        await seeder.ReviewableBriefAsync(ReviewableRevision());
        var listed = await host.GetDocumentAsync("/Review/Briefs");

        Assert.Equal(["1", "1", "0"], listed.QuerySelectorAll("[aria-labelledby='drafts'] .segmented-count").Select(count => count.TextContent));
        Assert.Equal("A week of housing news", Assert.Single(listed.QuerySelectorAll("[aria-labelledby='drafts'] tbody .cell-title")).TextContent);
    }

    [Fact]
    public async Task AnUnknownOrMalformedBriefIsNotFoundAsync()
    {
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|owner"));

        foreach (var id in new[] { new string('a', 64), "not-a-record" })
        {
            using var missing = await host.Client.GetAsync($"/Review/Briefs/{id}");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }

    [Fact]
    public async Task OnlyAConfiguredSubjectMayOpenTheBriefPagesAsync()
    {
        await seeder.ReleaseCitedRecordsAsync();
        await seeder.ReviewableBriefAsync(ReviewableRevision());

        foreach (var url in new[] { "/Review/Briefs", $"/Review/Briefs/{RecordId}" })
        {
            using var anonymous = await host.Client.GetAsync(url);
            Assert.NotEqual(HttpStatusCode.OK, anonymous.StatusCode);
            using var stranger = new HttpRequestMessage(HttpMethod.Get, url);
            stranger.Headers.Add("Cookie", host.CreateCookie("auth0|stranger"));
            using var refused = await host.Client.SendAsync(stranger);
            Assert.NotEqual(HttpStatusCode.OK, refused.StatusCode);
        }
    }

    [Fact]
    public async Task TheEditorShowsTheExactCitedRevisionInlineWithALinkToTheReviewPageOfEachRecord()
    {
        await seeder.ReleaseCitedRecordsAsync();
        await seeder.ReviewableBriefAsync(ReviewableRevision());
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var page = await host.GetDocumentAsync($"/Review/Briefs/{RecordId}");

        Assert.Equal("Record ID " + RecordId, page.QuerySelector(".page-header-record-id")!.TextContent);
        var evidence = page.QuerySelector("[aria-labelledby='evidence']")!;
        Assert.Contains("Rent cap", evidence.TextContent, StringComparison.Ordinal);
        Assert.Contains("Summary of Rent cap", evidence.TextContent, StringComparison.Ordinal);
        Assert.NotNull(evidence.QuerySelector($"a[href='/Review/Statements/{CitedStatement}']"));
        Assert.NotNull(evidence.QuerySelector($"a[href='/Review/Actions/{CitedAction}']"));
        Assert.NotNull(evidence.QuerySelector($"a[href='/Review/{CitedDocumentChange}']"));
        Assert.Null(page.QuerySelector("#cited-changed"));
    }

    [Fact]
    public async Task AChangedCitedRecordShowsTheFlagOnTheListAndInTheEditorWhileTheBriefStaysApproved()
    {
        await seeder.ReleaseCitedRecordsAsync();
        await seeder.ReviewableBriefAsync(ReviewableRevision());
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));
        var decision = Fields((await host.GetDocumentAsync($"/Review/Briefs/{RecordId}")).QuerySelector("#decision-form")!);
        decision.Add(new("kind", "Approve"));
        using var approved = await host.Client.PostAsync($"/Review/Briefs/{RecordId}", new FormUrlEncodedContent(decision));
        Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);
        await seeder.DecisionAsync(PublishedRecordKind.Statement, CitedStatement, 1, 2, ReviewDecisionKind.WithdrawApproval);

        var list = await host.GetDocumentAsync("/Review/Briefs");
        var page = await host.GetDocumentAsync($"/Review/Briefs/{RecordId}");

        Assert.Contains("Cited records changed", list.QuerySelector("[aria-labelledby='drafts'] tbody")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Withdraw the approval", page.QuerySelector("#cited-changed")!.TextContent, StringComparison.Ordinal);
        Assert.Equal(1, page.QuerySelectorAll("[aria-labelledby='evidence'] .badge").Count(badge => badge.TextContent == "Changed"));
        Assert.Contains("Approved, private", page.QuerySelector(".page-header")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASaveBindsEachParagraphToItsTickedCitationsAndDropsACitationNoParagraphKeeps()
    {
        await seeder.ReleaseCitedRecordsAsync();
        await seeder.ReviewableBriefAsync(ReviewableRevision());
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));
        var url = $"/Review/Briefs/{RecordId}";
        var fields = Fields((await host.GetDocumentAsync(url)).QuerySelector("#draft-form")!);
        Assert.Contains(fields, field => field.Key == "Input.Paragraphs[2].Text");
        Set(fields, "Input.Headline", "Person headline");
        Set(fields, "Input.Paragraphs[0].Text", "Person paragraph.");
        fields.RemoveAll(field => field.Key == "Input.Paragraphs[1].CitationIds" && field.Value == "R3");
        fields.RemoveAll(field => field.Key == "Input.Paragraphs[1].Text");
        fields.Add(new("Input.Paragraphs[1].Text", ""));
        fields.RemoveAll(field => field.Key == "Input.Paragraphs[1].CitationIds");

        using var saved = await host.Client.PostAsync(url, new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var page = await host.GetDocumentAsync(url);
        var cited = page.QuerySelectorAll("[aria-labelledby='evidence'] li strong").Select(item => item.TextContent);
        Assert.Equal(["R1"], cited);
        Assert.Contains("Person paragraph.", page.QuerySelector("#saved-approval-target")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStaleSaveReturnsConflictKeepsTheTypedWordingAndShowsBothVersions()
    {
        await seeder.ReleaseCitedRecordsAsync();
        await seeder.ReviewableBriefAsync(ReviewableRevision());
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));
        var url = $"/Review/Briefs/{RecordId}";
        var fields = Fields((await host.GetDocumentAsync(url)).QuerySelector("#draft-form")!);
        using var first = await host.Client.PostAsync(url, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);

        Set(fields, "Input.Headline", "Recovered unsaved headline");
        Set(fields, "idempotencyKey", Guid.NewGuid().ToString("N"));
        using var stale = await host.Client.PostAsync(url, new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var conflict = await new HtmlParser().ParseDocumentAsync(await stale.Content.ReadAsStringAsync());
        Assert.Equal("Recovered unsaved headline", conflict.QuerySelector("#Input_Headline")!.GetAttribute("value"));
        Assert.Equal("edit", conflict.QuerySelector("[data-tabset]")!.GetAttribute("data-selected"));
        Assert.Contains("Recovered unsaved headline", conflict.QuerySelector("[aria-labelledby='conflict-compare']")!.TextContent, StringComparison.Ordinal);
        Assert.True(conflict.QuerySelector("#decision-controls")!.HasAttribute("disabled"));
    }

    [Fact]
    public async Task ApprovalWithAChangedCitedRecordIsRefusedWithABadRequestAndNoDecision()
    {
        await seeder.ReleaseCitedRecordsAsync();
        await seeder.ReviewableBriefAsync(ReviewableRevision());
        await seeder.DecisionAsync(PublishedRecordKind.Statement, CitedStatement, 1, 2, ReviewDecisionKind.WithdrawApproval);
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));
        var url = $"/Review/Briefs/{RecordId}";
        var fields = Fields((await host.GetDocumentAsync(url)).QuerySelector("#decision-form")!);
        fields.Add(new("kind", "Approve"));

        using var refused = await host.Client.PostAsync(url, new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task AnInvalidSaveIsABadRequestWithNoNewRevisionAndAForgedExpectedRevisionTakesTheComparePath()
    {
        await seeder.ReleaseCitedRecordsAsync();
        await seeder.ReviewableBriefAsync(ReviewableRevision());
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));
        var url = $"/Review/Briefs/{RecordId}";
        var original = Fields((await host.GetDocumentAsync(url)).QuerySelector("#draft-form")!);

        var uncited = Copy(original);
        uncited.RemoveAll(field => field.Key == "Input.Paragraphs[0].CitationIds");
        var tooMany = Copy(original);
        for (var index = 2; index <= 12; index++)
        {
            tooMany.Add(new($"Input.Paragraphs[{index}].Text", $"Paragraph {index}."));
            tooMany.Add(new($"Input.Paragraphs[{index}].CitationIds", "R1"));
        }
        tooMany.RemoveAll(field => field.Key == "Input.Paragraphs[2].Text" && field.Value == "");
        var forgedCitation = Copy(original);
        forgedCitation.Add(new("Input.Paragraphs[0].CitationIds", "R9"));

        foreach (var invalid in new[] { uncited, tooMany, forgedCitation })
        {
            Set(invalid, "idempotencyKey", Guid.NewGuid().ToString("N"));
            using var refused = await host.Client.PostAsync(url, new FormUrlEncodedContent(invalid));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }
        Assert.Equal(1L, await seeder.ScalarAsync("SELECT count(*) FROM brief_revisions"));

        var forgedRevision = Copy(original);
        Set(forgedRevision, "expectedRevisionNumber", "7");
        Set(forgedRevision, "idempotencyKey", Guid.NewGuid().ToString("N"));
        using var stale = await host.Client.PostAsync(url, new FormUrlEncodedContent(forgedRevision));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("conflict-compare", await stale.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AConflictClearsEachTickedCitationThatNowNamesAnotherRecordSoTheReviewerMustChooseAgain()
    {
        await seeder.ReleaseCitedRecordsAsync();
        await seeder.StatementAsync(StatementId('9'), 1, "Another statement", "warren");
        await seeder.ReviewableBriefAsync(ReviewableRevision());
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));
        var url = $"/Review/Briefs/{RecordId}";
        var fields = Fields((await host.GetDocumentAsync(url)).QuerySelector("#draft-form")!);
        Set(fields, "Input.Paragraphs[0].Text", "Warren backed a rent cap.");
        var current = ReviewableRevision();
        await seeder.AppendRevisionAsync(current with
        {
            RevisionNumber = 2,
            CreatedAtUtc = Created.AddHours(1),
            Citations = [current.Citations[0] with { RecordId = StatementId('9') }, .. current.Citations.Skip(1)]
        });

        Set(fields, "idempotencyKey", Guid.NewGuid().ToString("N"));
        using var conflict = await host.Client.PostAsync(url, new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var page = await new HtmlParser().ParseDocumentAsync(await conflict.Content.ReadAsStringAsync());
        Assert.Equal([], Checked(page, 0));
        Assert.Equal(["R2", "R3"], Checked(page, 1));
        Assert.Contains("Another statement", page.QuerySelector("[aria-labelledby='conflict-compare']")!.TextContent, StringComparison.Ordinal);
        var resubmit = Fields(page.QuerySelector("#draft-form")!);
        Set(resubmit, "idempotencyKey", Guid.NewGuid().ToString("N"));
        using var refused = await host.Client.PostAsync(url, new FormUrlEncodedContent(resubmit));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(2L, await seeder.ScalarAsync("SELECT count(*) FROM brief_revisions"));
    }

    [Fact]
    public async Task TheSuggestionPanelShowsTheHeadlineOfEachRecordItCitesEvenWhenTheBriefDoesNotCiteIt()
    {
        await SeedPersonBriefWithSuggestionAsync();
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var page = await host.GetDocumentAsync($"/Review/Briefs/{RecordId}");

        var panel = page.QuerySelector("#suggestion")!.TextContent;
        Assert.Contains("Suggested paragraph. [Suggested record]", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("Rent cap", panel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UseSuggestionSavesItsHeadlineParagraphsAndCitationsAsTheNextRevisionAndClearsIt()
    {
        await SeedPersonBriefWithSuggestionAsync();
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));
        var url = $"/Review/Briefs/{RecordId}";
        var fields = Fields((await host.GetDocumentAsync(url)).QuerySelector("#draft-form")!);
        fields.Add(new("Input.SuggestionAction", "use"));

        using var saved = await host.Client.PostAsync(url, new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Equal(2L, await seeder.ScalarAsync("SELECT count(*) FROM brief_revisions"));
        var page = await host.GetDocumentAsync(url);
        var revision = page.QuerySelector("#saved-approval-target")!.TextContent;
        Assert.Contains("Suggested headline", revision, StringComparison.Ordinal);
        Assert.Contains("Suggested paragraph.", revision, StringComparison.Ordinal);
        Assert.DoesNotContain("Person paragraph.", revision, StringComparison.Ordinal);
        Assert.Equal(["R1"], page.QuerySelectorAll("[aria-labelledby='evidence'] li strong").Select(item => item.TextContent));
        Assert.Contains("Suggested record", page.QuerySelector("[aria-labelledby='evidence']")!.TextContent, StringComparison.Ordinal);
        Assert.Null(page.QuerySelector("#suggestion"));
    }

    private async Task SeedPersonBriefWithSuggestionAsync()
    {
        var suggestedStatement = StatementId('8');
        await seeder.ReleaseCitedRecordsAsync();
        await seeder.StatementAsync(suggestedStatement, 1, "Suggested record", "warren");
        var current = ReviewableRevision();
        var suggestion = new BriefTextSuggestion("Suggested headline", [new("Suggested paragraph.", ["R1"])],
            [new("R1", BriefCitationKind.Statement, suggestedStatement, 1)], new string('1', 32), Fingerprint);
        await seeder.ReviewableBriefAsync(current with
        {
            TextOrigin = ReviewTextOrigin.Person,
            Paragraphs = [new("Person paragraph.", ["R1", "R2", "R3"])],
            Suggestion = suggestion
        });
    }

    private static List<KeyValuePair<string, string>> Copy(List<KeyValuePair<string, string>> fields) => [.. fields];

    private static string[] Checked(IDocument page, int paragraph) => [.. page.QuerySelectorAll($"#draft-form input[name='Input.Paragraphs[{paragraph}].CitationIds'][checked]")
        .Select(input => input.GetAttribute("value")!)];
}
