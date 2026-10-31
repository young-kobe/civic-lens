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
    public async Task TheRailLinksTheRelationshipListWhichCountsEachFilterAndListsTheLinkWithBothHeadlines()
    {
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var empty = await host.GetDocumentAsync("/Review/Relationships");

        Assert.Equal("page", empty.QuerySelector(".nav a[href='/Review/Relationships']")!.GetAttribute("aria-current"));
        Assert.Null(empty.QuerySelector(".nav a[href='/Review']")!.GetAttribute("aria-current"));
        Assert.Equal(["0", "0", "0"], empty.QuerySelectorAll("[aria-labelledby='drafts'] .segmented-count").Select(count => count.TextContent));
        Assert.Equal("No relationships yet", empty.QuerySelector("[aria-labelledby='drafts'] .empty h3")!.TextContent);

        await SeedAsync();
        var listed = await host.GetDocumentAsync("/Review/Relationships");

        Assert.Equal(["1", "1", "0"], listed.QuerySelectorAll("[aria-labelledby='drafts'] .segmented-count").Select(count => count.TextContent));
        var row = Assert.Single(listed.QuerySelectorAll("[aria-labelledby='drafts'] tbody tr"));
        Assert.Contains("Describes own bill", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("Warren on rent", row.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Cited records changed", row.TextContent, StringComparison.Ordinal);
    }

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

        foreach (var page in new[] { "/Review/Relationships", url })
        {
            using var anonymous = await host.Client.GetAsync(page);
            Assert.NotEqual(HttpStatusCode.OK, anonymous.StatusCode);
            using var stranger = new HttpRequestMessage(HttpMethod.Get, page);
            stranger.Headers.Add("Cookie", host.CreateCookie("auth0|stranger"));
            using var refused = await host.Client.SendAsync(stranger);
            Assert.NotEqual(HttpStatusCode.OK, refused.StatusCode);
        }
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
    public async Task AChangedCitedRecordShowsTheFlagOnTheListAndInTheEditorAndRefusesApprovalWhileAnApprovedLinkStaysApproved()
    {
        await SeedAsync();
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));
        await seeder.StatementAsync(Statement(revision: 2));

        var list = await host.GetDocumentAsync("/Review/Relationships");
        var page = await host.GetDocumentAsync(url);
        var decision = Fields(page.QuerySelector("#decision-form")!);
        decision.Add(new("kind", "Approve"));
        using var refused = await host.Client.PostAsync(url, new FormUrlEncodedContent(decision));

        Assert.Contains("Cited records changed", list.QuerySelector("[aria-labelledby='drafts'] tbody")!.TextContent, StringComparison.Ordinal);
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
    public async Task ANoLongerMatchingPairShowsABadgeOnTheListAndANoticeInTheEditor()
    {
        await SeedAsync();
        await seeder.ExecuteAsync("UPDATE relationship_drafts SET no_longer_matches = true");
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var list = await host.GetDocumentAsync("/Review/Relationships");
        var page = await host.GetDocumentAsync(url);

        Assert.Contains("No longer matches", list.QuerySelector("[aria-labelledby='drafts'] tbody")!.TextContent, StringComparison.Ordinal);
        Assert.NotNull(page.QuerySelector("#no-longer-matches"));
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
