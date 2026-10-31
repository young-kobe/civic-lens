using System.Net;
using AngleSharp.Html.Parser;
using CivicLens.Core.Documents;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using static CivicLens.Tests.Fixtures.RelationshipFixtures;
using static CivicLens.Tests.Host.ReviewWorkspaceHost;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class RelationshipReviewWorkspaceTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string FlagText = "This statement and this action or vote no longer share a bill.";
    private const string CheckBeforeText = "Check the link before you approve it.";
    private const string CheckApprovedText =
        "Open the link and check it. Withdraw the approval if it is wrong. If it is right, it stays on the site and this row stays here.";

    private readonly ReviewWorkspaceHost host = new(postgres);
    private RelationshipWorldSeeder seeder = null!;
    private string url = null!;

    public async Task InitializeAsync()
    {
        await host.StartAsync();
        seeder = new RelationshipWorldSeeder(host.ConnectionString);
        url = $"/Review/Relationships/{ActionPair().PairId}";
    }

    public Task DisposeAsync() => host.StopAsync();

    [Fact]
    public async Task TheLookupBoxOpensTheEditorOfARelationshipIdAndTheEditorShowsTheIdToCopy()
    {
        await SeedAsync();
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        using var found = await host.Client.GetAsync($"/Review?find=%20{ActionPair().PairId}%20");
        var editor = await host.GetDocumentAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, found.StatusCode);
        Assert.Equal(url, found.Headers.Location!.AbsolutePath);
        Assert.Equal("Record ID " + ActionPair().PairId, editor.QuerySelector(".page-header-record-id")!.TextContent);
        Assert.Equal("/Review", Assert.Single(editor.QuerySelectorAll(".rail .nav-link"), link => link.GetAttribute("aria-current") == "page").GetAttribute("href"));
    }

    [Fact]
    public async Task AnUnknownOrMalformedRelationshipIsNotFoundAndOnlyAConfiguredSubjectMayOpenThePages()
    {
        await SeedAsync();
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|owner"));
        foreach (var id in new[] { new string('a', 64), "not-a-record" })
        {
            using var missing = await host.Client.GetAsync($"/Review/Relationships/{id}");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
        host.Client.DefaultRequestHeaders.Remove("Cookie");

        using var anonymous = await host.Client.GetAsync(url);
        Assert.NotEqual(HttpStatusCode.OK, anonymous.StatusCode);
        using var stranger = new HttpRequestMessage(HttpMethod.Get, url);
        stranger.Headers.Add("Cookie", host.CreateCookie("auth0|stranger"));
        using var refused = await host.Client.SendAsync(stranger);
        Assert.NotEqual(HttpStatusCode.OK, refused.StatusCode);
    }

    [Fact]
    public async Task TheEditorShowsTheStatementPassageAndTheTargetFactsSideBySideWithTheCitedRevisionsAndTheMatch()
    {
        await SeedAsync();
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var page = await host.GetDocumentAsync(url);

        var statement = page.QuerySelector("[aria-labelledby='statement']")!.TextContent;
        Assert.Contains(BillWords, statement, StringComparison.Ordinal);
        Assert.Contains("Bill number", statement, StringComparison.Ordinal);
        Assert.Equal("1", CitedRevision(page, "statement"));
        var target = page.QuerySelector("[aria-labelledby='target']")!.TextContent;
        Assert.Contains("Bill: ", target, StringComparison.Ordinal);
        Assert.Equal("1", CitedRevision(page, "target"));
        Assert.NotNull(page.QuerySelector($"a[href='/Review/Statements/{StatementId}']"));
        Assert.NotNull(page.QuerySelector($"a[href='/Review/Actions/{ActionId}']"));
        Assert.Equal(["describesOwnBill", "statesPositionOn", "claimsCreditFor"],
            page.QuerySelectorAll("#draft-form input[name='Input.Type']").Select(input => input.GetAttribute("value")!));
        Assert.Null(page.QuerySelector("#cited-changed"));
    }

    [Fact]
    public async Task AChangedCitedRecordShowsTheFlagInTheEditorAndRefusesApprovalWhileAnApprovedLinkStaysApproved()
    {
        await SeedAsync();
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));
        await seeder.StatementAsync(Statement(revision: 2));

        var page = await host.GetDocumentAsync(url);
        var decision = Fields(page.QuerySelector("#decision-form")!);
        decision.Add(new("kind", "Approve"));
        using var refused = await host.Client.PostAsync(url, new FormUrlEncodedContent(decision));

        Assert.Contains("Approval is blocked", page.QuerySelector("#cited-changed")!.TextContent, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        await seeder.StatementAsync(Statement());
        Set(decision, "idempotencyKey", Guid.NewGuid().ToString("N"));
        using var approved = await host.Client.PostAsync(url, new FormUrlEncodedContent(decision));
        Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);

        await seeder.StatementAsync(Statement(revision: 2));
        var after = await host.GetDocumentAsync(url);
        Assert.Contains("stays approved", after.QuerySelector("#cited-changed")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Approved, private", after.QuerySelector(".page-header")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASaveChangesTheTypeReasonQuoteAndFactFromTheOfferedChoicesAndAForgedQuoteIsABadRequest()
    {
        await SeedAsync();
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));
        var fields = Fields((await host.GetDocumentAsync(url)).QuerySelector("#draft-form")!);
        var passage = Span(BillSentence);
        var forged = new List<KeyValuePair<string, string>>(fields);
        Set(forged, "Input.QuoteSelection", $"{passage.Start - 5}:{passage.Length + 5}");
        Set(forged, "idempotencyKey", Guid.NewGuid().ToString("N"));
        using var refused = await host.Client.PostAsync(url, new FormUrlEncodedContent(forged));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        Set(fields, "Input.Type", "claimsCreditFor");
        Set(fields, "Input.Reason", "She claims credit.");
        Set(fields, "Input.QuoteSelection", $"{passage.Start}:{passage.Length}");
        Set(fields, "Input.FactId", "F1");
        using var saved = await host.Client.PostAsync(url, new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var page = await host.GetDocumentAsync(url);
        var revision = page.QuerySelector("#saved-approval-target")!.TextContent;
        Assert.Contains("Claims credit for", revision, StringComparison.Ordinal);
        Assert.Contains("She claims credit.", revision, StringComparison.Ordinal);
        Assert.Equal("2", (await seeder.ReadAsync("SELECT count(*) FROM relationship_revisions")).Single().Single());
    }

    [Fact]
    public async Task AStaleSaveReturnsConflictKeepsTheTypedWordingAndShowsBothVersions()
    {
        await SeedAsync();
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));
        var fields = Fields((await host.GetDocumentAsync(url)).QuerySelector("#draft-form")!);
        using var first = await host.Client.PostAsync(url, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);

        Set(fields, "Input.Reason", "Recovered unsaved reason");
        Set(fields, "idempotencyKey", Guid.NewGuid().ToString("N"));
        using var stale = await host.Client.PostAsync(url, new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var conflict = await new HtmlParser().ParseDocumentAsync(await stale.Content.ReadAsStringAsync());
        Assert.Equal("Recovered unsaved reason", conflict.QuerySelector("#Input_Reason")!.GetAttribute("value"));
        Assert.Equal("edit", conflict.QuerySelector("[data-tabset]")!.GetAttribute("data-selected"));
        Assert.Contains("Recovered unsaved reason", conflict.QuerySelector("[aria-labelledby='conflict-compare']")!.TextContent, StringComparison.Ordinal);
        Assert.True(conflict.QuerySelector("#decision-controls")!.HasAttribute("disabled"));
    }

    [Fact]
    public async Task TheReviewHomeQueueListsTheBriefAndTheRelationshipThatNeedActionAndLinksEachEditor()
    {
        await SeedAsync();
        var briefSeeder = new BriefWorldSeeder(host.ConnectionString);
        var waiting = BriefFixtures.ReviewableRevision(new DateOnly(2026, 10, 5));
        await briefSeeder.ReviewableBriefAsync(waiting);
        await briefSeeder.ReviewableBriefAsync(BriefFixtures.ReviewableRevision(new DateOnly(2026, 10, 12)), "Approved");
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var home = await host.GetDocumentAsync("/Review");

        var links = home.QuerySelectorAll("[aria-labelledby='needs-a-person'] tbody a.cell-title").Select(link => link.GetAttribute("href")).ToArray();
        Assert.Equal(2, links.Length);
        Assert.Contains(url, links);
        Assert.Contains($"/Review/Briefs/{waiting.RecordId}", links);
    }

    [Fact]
    public async Task ANoLongerMatchingPairShowsANoticeInTheEditor()
    {
        await SeedAsync();
        await seeder.ExecuteAsync("UPDATE relationship_drafts SET no_longer_matches = true");
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var page = await host.GetDocumentAsync(url);

        Assert.NotNull(page.QuerySelector("#no-longer-matches"));
    }

    [Fact]
    public async Task AnApprovedLinkThatNoLongerMatchesShowsToTheOwnerOnlyAndBecomesAnOrdinaryQueueRowAfterTheApprovalIsWithdrawn()
    {
        await SeedAsync();
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));
        await PostDecisionAsync("Approve");

        Assert.DoesNotContain(FlagText, await OwnerQueueTextAsync(), StringComparison.Ordinal);

        await seeder.ExecuteAsync("UPDATE relationship_drafts SET no_longer_matches = true");
        var shown = await OwnerQueueTextAsync();
        Assert.Contains(FlagText + " " + CheckApprovedText, shown, StringComparison.Ordinal);
        Assert.DoesNotContain("Showing the newest", shown, StringComparison.Ordinal);
        Assert.Equal(1, Links(await OwnerQueueHtmlAsync()));
        Assert.DoesNotContain(FlagText, (await host.GetDocumentAsync("/Review")).QuerySelector("[aria-labelledby='needs-a-person']")!.TextContent,
            StringComparison.Ordinal);
        var editor = await host.GetDocumentAsync(url);
        Assert.Contains(CheckApprovedText, editor.QuerySelector("#no-longer-matches")!.TextContent, StringComparison.Ordinal);

        await PostDecisionAsync("WithdrawApproval", "The pair is wrong.");
        var after = await OwnerQueueTextAsync();
        Assert.DoesNotContain(FlagText, after, StringComparison.Ordinal);
        Assert.Contains("A reviewer withdrew the approval. Review the draft again.", after, StringComparison.Ordinal);
        Assert.Equal(1, Links(await OwnerQueueHtmlAsync()));
        var reopened = await host.GetDocumentAsync(url);
        Assert.Contains(CheckBeforeText, reopened.QuerySelector("#no-longer-matches")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MoreUnmatchedApprovedLinksThanRowsShowTheOwnerHowManyAreHiddenAndTheTotalCountsAllOfThem()
    {
        await SeedAsync();
        for (var index = 1; index <= 13; index++) await seeder.RevisionAsync(Revision() with { RecordId = Id(index) });
        await seeder.ExecuteAsync("UPDATE relationship_drafts SET review_status = 'Approved', no_longer_matches = true");
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|owner"));

        var panel = (await host.GetDocumentAsync("/Review")).QuerySelector("[aria-labelledby='needs-a-person']")!;

        Assert.Equal("Showing the newest 10 of 14 approved links that no longer match.", panel.QuerySelector("#unmatched-overflow")!.TextContent);
        Assert.Equal(10, panel.QuerySelectorAll("tbody tr").Length);
        Assert.Contains("14 records", panel.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSecondQueuePageHidesTheUnmatchedLinkRowsAndTheirOverflowLine()
    {
        await SeedAsync();
        await seeder.ExecuteAsync("UPDATE relationship_drafts SET review_status = 'Approved', no_longer_matches = true");
        for (var index = 1; index <= 11; index++) await seeder.RevisionAsync(Revision() with { RecordId = Id(index) });
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|owner"));

        var first = await host.GetDocumentAsync("/Review");
        var older = first.QuerySelector("nav[aria-label='Needs a person pages'] a[rel='next']")!.GetAttribute("href")!;
        var second = await host.GetDocumentAsync(older);

        Assert.Contains(FlagText, first.QuerySelector("[aria-labelledby='needs-a-person']")!.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(FlagText, second.QuerySelector("[aria-labelledby='needs-a-person']")!.TextContent, StringComparison.Ordinal);
    }

    private static string Id(int index) => index.ToString("x").PadLeft(64, 'a');

    private static int Links(string html) => new HtmlParser().ParseDocument("<body>" + html).QuerySelectorAll($"tbody a.cell-title[href='/Review/Relationships/{ActionPair().PairId}']").Length;

    private async Task PostDecisionAsync(string kind, string? note = null)
    {
        var fields = Fields((await host.GetDocumentAsync(url)).QuerySelector("#decision-form")!);
        Set(fields, "kind", kind);
        if (note is not null) Set(fields, "Decision.Note", note);
        using var response = await host.Client.PostAsync(url, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private async Task<string> OwnerQueueHtmlAsync()
    {
        var friend = host.Client.DefaultRequestHeaders.GetValues("Cookie").Single();
        host.Client.DefaultRequestHeaders.Remove("Cookie");
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|owner"));
        var panel = (await host.GetDocumentAsync("/Review")).QuerySelector("[aria-labelledby='needs-a-person']")!;
        host.Client.DefaultRequestHeaders.Remove("Cookie");
        host.Client.DefaultRequestHeaders.Add("Cookie", friend);
        return panel.InnerHtml;
    }

    private async Task<string> OwnerQueueTextAsync()
    {
        var html = await OwnerQueueHtmlAsync();
        return new HtmlParser().ParseDocument("<body>" + html).Body!.TextContent;
    }

    private static string CitedRevision(AngleSharp.Dom.IDocument page, string panel) => page.QuerySelectorAll($"[aria-labelledby='{panel}'] dt")
        .Single(label => label.TextContent == "Cited revision").NextElementSibling!.TextContent;

    private async Task SeedAsync()
    {
        await seeder.StatementAsync(Statement());
        await seeder.ActionAsync(Action());
        await seeder.RevisionAsync(Revision() with { StatementQuote = Span(BillWords) });
    }
}
