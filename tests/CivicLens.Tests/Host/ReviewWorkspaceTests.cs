using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.Core.Documents;
using CivicLens.Tests.Infrastructure.Collection;
using static CivicLens.Tests.Host.ReviewWorkspaceHost;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ReviewWorkspaceTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Owner = "auth0|owner";
    private const string Reviewer = "auth0|friend";
    private readonly ReviewWorkspaceHost host = new(postgres);

    public Task InitializeAsync() => host.StartAsync();

    public Task DisposeAsync() => host.StopAsync();

    [Fact]
    public async Task ForgedPostsCachedPagesAndForeignHostsAreRefusedAsync()
    {
        var comparison = await SaveComparisonAsync("Deadline: October 15.\n", "Deadline: October 30.\n");
        SignIn(Owner);
        using var page = await host.Client.GetAsync("/Review");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        // Unpublished evidence must never sit in a shared or browser cache.
        Assert.True(page.Headers.CacheControl?.NoStore);

        // A cross-site page can make the browser post the reviewer's cookie, but never the antiforgery token.
        var home = await host.GetDocumentAsync("/Review");
        var forged = StartFields(home, comparison.ComparisonId).Where(field => field.Key != "__RequestVerificationToken");
        using var rejected = await host.Client.PostAsync("/Review", new FormUrlEncodedContent(forged));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains(comparison.ComparisonId, (await host.GetDocumentAsync("/Review")).QuerySelector("#start-draft")!.ParentElement!.InnerHtml);

        // Sign-out changes state, so it must be a token-checked POST and never a link a third party can trigger.
        using var logoutGet = await host.Client.GetAsync("/Review/Logout");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, logoutGet.StatusCode);
        using var logoutPost = await host.Client.PostAsync("/Review/Logout", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, logoutPost.StatusCode);

        // The cookie is bound to one origin; a rebound DNS name must not reach the workspace.
        using var foreign = new HttpRequestMessage(HttpMethod.Get, "/Review");
        foreign.Headers.Host = "attacker.example.test";
        using var refused = await host.Client.SendAsync(foreign);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task StartingADraftOpensItsEditorOnceAndRemovesTheChangeFromNewChangesAsync()
    {
        var comparison = await SaveComparisonAsync("Fee: 10 dollars.\n", "Fee: 12 dollars.\n");
        SignIn(Owner);
        var home = await host.GetDocumentAsync("/Review");
        var start = new FormUrlEncodedContent(StartFields(home, comparison.ComparisonId));
        var body = await start.ReadAsStringAsync();

        using var created = await host.Client.PostAsync("/Review", new StringContent(body, null, "application/x-www-form-urlencoded"));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var editorUrl = PathOf(created);
        Assert.Matches("^/Review/[0-9a-f]{32}$", editorUrl);

        using var replay = await host.Client.PostAsync("/Review", new StringContent(body, null, "application/x-www-form-urlencoded"));
        Assert.Equal(editorUrl, PathOf(replay));

        var editor = await host.GetDocumentAsync(editorUrl);
        Assert.Equal("Untitled draft", editor.QuerySelector("#saved-approval-target h2")!.TextContent);
        var missingUrl = "/Review/" + Guid.NewGuid().ToString("N");
        using var missing = await host.Client.GetAsync(missingUrl);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var missingSave = await host.Client.PostAsync(missingUrl, Form(editor.QuerySelector("#draft-form")!));
        Assert.Equal(HttpStatusCode.NotFound, missingSave.StatusCode);

        home = await host.GetDocumentAsync("/Review");
        Assert.Empty(ChangeIds(home));
        Assert.Equal([editorUrl], DraftUrls(home));
        Assert.Equal("No new changes", home.QuerySelector(".empty h3")!.TextContent);
        Assert.Contains("You started an untitled draft.", home.QuerySelector(".feed")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaleSaveKeepsUnsavedWordingApartFromTheApprovalTargetAndBlocksDecisionsAsync()
    {
        await SaveComparisonAsync("Before policy.\n", "After policy.\n");
        SignIn(Owner);
        var editorUrl = await StartFirstDraftAsync();
        var editor = await host.GetDocumentAsync(editorUrl);
        Assert.Equal("false", editor.QuerySelector("[data-unsaved-form]")!.GetAttribute("data-recovered-unsaved"));
        Assert.Equal("saved", editor.QuerySelector("[data-tabset]")!.GetAttribute("data-selected"));
        var fields = Fields(editor.QuerySelector("#draft-form")!);
        Set(fields, "Input.Headline", "Saved headline");
        Set(fields, "Input.Summary", "Supported summary");
        Set(fields, "Input.Institution", "Example Office");
        using var saved = await host.Client.PostAsync(editorUrl, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        Set(fields, "Input.Headline", "Recovered unsaved headline");
        Set(fields, "idempotencyKey", Guid.NewGuid().ToString("N"));
        using var stale = await host.Client.PostAsync(editorUrl, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var conflict = await ParseAsync(stale);
        // The reviewer's typing must survive the conflict, but it must never pose as the saved wording they approve.
        Assert.Equal("Recovered unsaved headline", conflict.QuerySelector("#Input_Headline")!.GetAttribute("value"));
        Assert.Equal("true", conflict.QuerySelector("[data-unsaved-form]")!.GetAttribute("data-recovered-unsaved"));
        Assert.Equal("edit", conflict.QuerySelector("[data-tabset]")!.GetAttribute("data-selected"));
        var target = conflict.QuerySelector("#saved-approval-target")!.TextContent;
        Assert.Contains("Saved headline", target, StringComparison.Ordinal);
        Assert.DoesNotContain("Recovered unsaved headline", target, StringComparison.Ordinal);
        Assert.Contains("Recovered unsaved headline", conflict.QuerySelector("[aria-labelledby='conflict-compare']")!.TextContent, StringComparison.Ordinal);
        Assert.True(conflict.QuerySelector("#decision-controls")!.HasAttribute("disabled"));
        Assert.False(conflict.QuerySelector("#decision-unsaved-warning")!.HasAttribute("hidden"));

        editor = await host.GetDocumentAsync(editorUrl);
        Assert.False(editor.QuerySelector("#decision-controls")!.HasAttribute("disabled"));
        Assert.True(editor.QuerySelector("#decision-unsaved-warning")!.HasAttribute("hidden"));
    }

    [Fact]
    public async Task DecisionsFollowTheReviewRulesAndInvalidOnesRedisplayTheSavedRevisionAsync()
    {
        await SaveComparisonAsync("Hours: 9 to 5.\n", "Hours: 10 to 4.\n");
        SignIn(Owner);
        var editorUrl = await StartFirstDraftAsync();
        await SaveReadyRevisionAsync(editorUrl, "Office hours shortened");

        using var missingReason = await DecideAsync(editorUrl, "RequestChanges");
        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);
        var invalid = await ParseAsync(missingReason);
        Assert.Equal("Office hours shortened", invalid.QuerySelector("#Input_Headline")!.GetAttribute("value"));
        Assert.True(invalid.QuerySelector("#decision-controls")!.HasAttribute("disabled"));

        using var requested = await DecideAsync(editorUrl, "RequestChanges", "Cite the notice.");
        Assert.Equal(HttpStatusCode.Redirect, requested.StatusCode);
        var editor = await host.GetDocumentAsync(editorUrl);
        Assert.Contains("Approval is blocked", editor.QuerySelector(".notice-warn")!.TextContent, StringComparison.Ordinal);
        var concern = editor.QuerySelector("input[name='Decision.ResolvedDecisionIds']")!;
        Assert.Equal("decision-form", concern.GetAttribute("form"));

        using var unresolved = await DecideAsync(editorUrl, "Approve", "Looks fine.");
        Assert.Equal(HttpStatusCode.BadRequest, unresolved.StatusCode);
        using var approved = await DecideAsync(editorUrl, "Approve", "The notice is cited.", [concern.GetAttribute("value")!]);
        Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);

        editor = await host.GetDocumentAsync(editorUrl);
        Assert.Equal("Approved, private", editor.QuerySelector(".page-header .badge")!.TextContent);
        Assert.Null(editor.QuerySelector("#decision-form button[value='Approve']"));
        Assert.NotNull(editor.QuerySelector("#decision-form button[value='WithdrawApproval']"));
        var history = editor.QuerySelector("[aria-labelledby='history'] tbody")!.TextContent;
        Assert.Contains("Changes requested", history, StringComparison.Ordinal);
        Assert.Contains("Cite the notice.", history, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetainedEvidenceIsShownAsTextNeverAsMarkupAsync()
    {
        const string untrustedSource = "<img src=x onerror=alert(1)>";
        var before = await host.SaveExtractionAsync("Earlier wording.\n");
        var after = await host.SaveExtractionAsync(untrustedSource + "\n");
        await host.Comparisons.SaveAsync(DocumentComparison.Create(before, after), default);
        SignIn(Owner);

        // Captured pages are attacker-controlled; rendering them as HTML would run script in the reviewer's session.
        using var homeResponse = await host.Client.GetAsync("/Review");
        var homeHtml = await homeResponse.Content.ReadAsStringAsync();
        Assert.Contains("&lt;img", homeHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", homeHtml, StringComparison.Ordinal);

        var editorUrl = await StartFirstDraftAsync();
        var editor = await host.GetDocumentAsync(editorUrl);
        var fields = Fields(editor.QuerySelector("#draft-form")!);
        Set(fields, "Input.Headline", "Saved public account");
        Set(fields, "Input.Summary", "A saved summary for review.");
        Set(fields, "Input.Institution", "Example Office");
        fields.RemoveAll(field => field.Key == "Input.EvidenceSelection");
        foreach (var option in editor.QuerySelectorAll("input[name='Input.EvidenceSelection']"))
            fields.Add(new("Input.EvidenceSelection", option.GetAttribute("value")!));
        using var saved = await host.Client.PostAsync(editorUrl, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        editor = await host.GetDocumentAsync(editorUrl);
        var citations = editor.QuerySelector("#saved-citations")!;
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", citations.InnerHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", editor.DocumentElement.OuterHtml, StringComparison.Ordinal);
        Assert.Contains("Earlier version", citations.TextContent, StringComparison.Ordinal);
        Assert.Contains("Later version", citations.TextContent, StringComparison.Ordinal);
        // Every quote must lead back to its exact retained span, so a reviewer can check it in context.
        var links = citations.QuerySelectorAll("a[href^='/Documents/Citation']").Select(link => link.GetAttribute("href")!).ToArray();
        Assert.Contains(links, link => link.Contains(before.ExtractionId, StringComparison.Ordinal));
        Assert.Contains(links, link => link.Contains(after.ExtractionId, StringComparison.Ordinal));
        Assert.All(citations.QuerySelectorAll("[data-link]"), quote =>
            Assert.NotNull(editor.QuerySelector($"[aria-labelledby='evidence'] [data-link='{quote.GetAttribute("data-link")}']")));
    }

    [Fact]
    public async Task OnlyTheOwnerSeesSourcesAndSourceHealthAsync()
    {
        SignIn(Reviewer);
        var reviewer = await host.GetDocumentAsync("/Review");
        Assert.Null(reviewer.QuerySelector("a[href='/Review/Sources']"));
        Assert.Null(reviewer.QuerySelector("#source-health"));
        Assert.DoesNotContain("Sources need attention", reviewer.QuerySelector(".stats")!.TextContent, StringComparison.Ordinal);

        host.Client.DefaultRequestHeaders.Remove("Cookie");
        SignIn(Owner);
        var owner = await host.GetDocumentAsync("/Review");
        Assert.NotNull(owner.QuerySelector("a[href='/Review/Sources']"));
        Assert.Contains("Test Official", owner.QuerySelector("[aria-labelledby='source-health']")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Sources need attention", owner.QuerySelector(".stats")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NewerAndOlderLinksVisitEveryRowOnceAndKeepTheOtherListInPlaceAsync()
    {
        var comparisons = new List<string>();
        for (var index = 0; index < 33; index++)
            comparisons.Add((await SaveComparisonAsync($"Version {index} before.\n", $"Version {index} after.\n")).ComparisonId);
        SignIn(Owner);
        var drafts = new List<string>();
        for (var index = 0; index < 22; index++) drafts.Add(await StartFirstDraftAsync());

        var changeWalk = await WalkAsync("/Review", "new-changes", ChangeIds);
        var draftWalk = await WalkAsync("/Review", "drafts", DraftUrls);

        var visitedChanges = changeWalk.Down.SelectMany(ids => ids).ToArray();
        Assert.Equal([10, 1], changeWalk.Down.Select(page => page.Length));
        Assert.Equal(visitedChanges.Length, visitedChanges.Distinct().Count());
        Assert.Subset(comparisons.ToHashSet(), visitedChanges.ToHashSet());
        Assert.Equal(drafts.Order(), draftWalk.Down.SelectMany(ids => ids).Order());
        Assert.Equal([10, 10, 2], draftWalk.Down.Select(page => page.Length));
        Assert.Equal(changeWalk.Down.SkipLast(1).Reverse().Select(Join), changeWalk.Up.Select(Join));
        Assert.Equal(draftWalk.Down.SkipLast(1).Reverse().Select(Join), draftWalk.Up.Select(Join));

        var secondChanges = await host.GetDocumentAsync(OlderHref(await host.GetDocumentAsync("/Review"), "new-changes")!);
        var olderDrafts = OlderHref(secondChanges, "drafts")!;
        var both = await host.GetDocumentAsync(olderDrafts);
        Assert.Equal(ChangeIds(secondChanges), ChangeIds(both));
        Assert.Equal(draftWalk.Down[1], DraftUrls(both));
    }

    [Fact]
    public async Task DraftFilterTabsMatchTheOverviewCountsAndFilterTheListAsync()
    {
        for (var index = 0; index < 3; index++) await SaveComparisonAsync($"Rule {index} old.\n", $"Rule {index} new.\n");
        SignIn(Owner);
        var approvedUrl = await StartFirstDraftAsync();
        await StartFirstDraftAsync();
        await StartFirstDraftAsync();
        await SaveReadyRevisionAsync(approvedUrl, "Rule changed");
        using var approved = await DecideAsync(approvedUrl, "Approve");
        Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);

        var home = await host.GetDocumentAsync("/Review");
        Assert.Equal(new Dictionary<string, string> { ["All"] = "3", ["Needs action"] = "2", ["Approved"] = "1" }, TabCounts(home));
        Assert.Equal("2", StatValue(home, "Drafts need action"));
        Assert.Equal("1", StatValue(home, "Approved, private"));
        Assert.Equal(3, DraftUrls(home).Length);

        var approvedOnly = await host.GetDocumentAsync(home.QuerySelector("a.stat[href*='drafts=approved']")!.GetAttribute("href")!);
        Assert.Equal([approvedUrl], DraftUrls(approvedOnly));
        Assert.Equal("Approved, private", approvedOnly.QuerySelector("[aria-labelledby='drafts'] tbody .badge")!.TextContent);
        Assert.Equal("true", approvedOnly.QuerySelector("[aria-labelledby='drafts'] a.segmented-item[href*='approved']")!.GetAttribute("aria-current"));

        var needsAction = await host.GetDocumentAsync("/Review?drafts=needs-action");
        Assert.Equal(2, DraftUrls(needsAction).Length);
        Assert.DoesNotContain(approvedUrl, DraftUrls(needsAction));
    }

    private void SignIn(string subject) => host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie(subject));

    private async Task<DocumentComparison> SaveComparisonAsync(string beforeText, string afterText)
    {
        var before = await host.SaveExtractionAsync(beforeText);
        var after = await host.SaveExtractionAsync(afterText);
        return await host.Comparisons.SaveAsync(DocumentComparison.Create(before, after), default);
    }

    private static List<KeyValuePair<string, string>> StartFields(IDocument home, string comparisonId)
    {
        Assert.NotNull(home.QuerySelector($"button[form='start-draft'][value='{comparisonId}']"));
        var fields = Fields(home.QuerySelector("#start-draft")!);
        fields.Add(new("comparisonId", comparisonId));
        return fields;
    }

    private async Task<string> StartFirstDraftAsync()
    {
        var home = await host.GetDocumentAsync("/Review");
        using var created = await host.Client.PostAsync("/Review", new FormUrlEncodedContent(StartFields(home, ChangeIds(home)[0])));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        return PathOf(created);
    }

    private async Task SaveReadyRevisionAsync(string editorUrl, string headline)
    {
        var editor = await host.GetDocumentAsync(editorUrl);
        var fields = Fields(editor.QuerySelector("#draft-form")!);
        Set(fields, "Input.Headline", headline);
        Set(fields, "Input.Summary", "The source changed its wording.");
        Set(fields, "Input.Institution", "Example Office");
        Assert.Contains(fields, field => field.Key == "Input.EvidenceSelection");
        using var saved = await host.Client.PostAsync(editorUrl, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
    }

    private async Task<HttpResponseMessage> DecideAsync(string editorUrl, string kind, string? note = null,
        IEnumerable<string>? resolved = null)
    {
        var editor = await host.GetDocumentAsync(editorUrl);
        var fields = Fields(editor.QuerySelector("#decision-form")!);
        Set(fields, "kind", kind);
        if (note is not null) Set(fields, "Decision.Note", note);
        foreach (var id in resolved ?? []) fields.Add(new("Decision.ResolvedDecisionIds", id));
        return await host.Client.PostAsync(editorUrl, new FormUrlEncodedContent(fields));
    }

    private async Task<(List<string[]> Down, List<string[]> Up)> WalkAsync(string start, string panel, Func<IDocument, string[]> ids)
    {
        var down = new List<string[]>();
        var page = await host.GetDocumentAsync(start);
        Assert.Null(Link(page, panel, "prev"));
        while (true)
        {
            down.Add(ids(page));
            var older = OlderHref(page, panel);
            if (older is null) break;
            page = await host.GetDocumentAsync(older);
            Assert.True(down.Count <= 10, "Older links did not end.");
        }
        var up = new List<string[]>();
        while (Link(page, panel, "prev") is { } newer)
        {
            page = await host.GetDocumentAsync(newer);
            up.Add(ids(page));
            Assert.True(up.Count <= 10, "Newer links did not end.");
        }
        return (down, up);
    }

    private static string? OlderHref(IDocument page, string panel) => Link(page, panel, "next");

    private static string? Link(IDocument page, string panel, string rel) =>
        page.QuerySelector($"[aria-labelledby='{panel}'] a[rel='{rel}']")?.GetAttribute("href");

    private static string[] ChangeIds(IDocument page) =>
        [.. page.QuerySelectorAll("button[form='start-draft']").Select(button => button.GetAttribute("value")!)];

    private static string[] DraftUrls(IDocument page) =>
        [.. page.QuerySelectorAll("[aria-labelledby='drafts'] a.cell-title").Select(link => link.GetAttribute("href")!)];

    private static Dictionary<string, string> TabCounts(IDocument page) =>
        page.QuerySelectorAll("[aria-labelledby='drafts'] a.segmented-item").ToDictionary(
            tab => tab.ChildNodes.OfType<IText>().First().TextContent.Trim(),
            tab => tab.QuerySelector(".segmented-count")!.TextContent);

    private static string StatValue(IDocument page, string label) =>
        page.QuerySelectorAll(".stat").Single(stat => stat.QuerySelector(".stat-label")!.TextContent == label)
            .QuerySelector(".stat-value")!.TextContent;

    private static string PathOf(HttpResponseMessage response) => response.Headers.Location!.IsAbsoluteUri
        ? response.Headers.Location.PathAndQuery : response.Headers.Location.ToString();

    private static string Join(string[] ids) => string.Join(',', ids);

    private static async Task<IDocument> ParseAsync(HttpResponseMessage response) =>
        await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());
}
