using System.Net;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicApi;
using CivicLens.PublicApi.Bills;
using CivicLens.PublicApi.ExecutiveActions;
using CivicLens.PublicApi.Pages;
using CivicLens.PublicApi.Votes;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Officials;
using CivicLens.Tests.PublicApi.Statements;
using CivicLens.Tests.PublicApi.Votes;
using CivicLens.Tests.PublicWeb;

namespace CivicLens.Tests.PublicApi.Pages;

/// <summary>The per-kind list pages: votes, bills, statements, and executive actions.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class RecordListPageTests(PostgresCollection postgres)
{
    private static readonly PublicBaseUrl BaseUrl = PublicBaseUrl.Parse("https://civic-lens.test", "test")!;
    private static readonly DateTimeOffset PublishedAt = new(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);
    private const string ModelLabel = "An AI summary of the official record";

    private async Task<ApiHost> StartAsync()
    {
        var host = await ApiHost.StartAsync(postgres, options => options with { PublicBaseUrl = BaseUrl });
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        return host;
    }

    private static async Task<IDocument> GetPageAsync(ApiHost host, string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await host.Client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{path} gave {(int)response.StatusCode}: {body}");
        return await new HtmlParser().ParseDocumentAsync(body);
    }

    private static IElement[] Rows(IDocument page) => [.. page.QuerySelectorAll(".record-row")];

    private static string? Label(IElement row) => row.QuerySelector(".ai-label") is { } label ? AiLabelAssert.FullText(label) : null;

    private static string Text(IElement element) => element.TextContent;

    private static string[] Titles(IDocument page) => [.. Rows(page).Select(row => row.QuerySelector("a > span")!.TextContent)];

    private static string[] Chips(IDocument page) => [.. page.QuerySelectorAll("ul[aria-label='Active filters'] .badge").Select(chip => chip.TextContent)];

    private static string Lede(IDocument page) => page.QuerySelector(".page-header-lede")!.TextContent;

    // ---- shared frame: tabs, header, count line ----

    [Fact]
    public async Task EveryListPageNamesItsKindInTheTabsAndTheHeaderLinksLeadToTheLists()
    {
        await using var host = await StartAsync();
        string[] paths = ["/votes.html", "/bills.html", "/statements.html", "/executive-actions.html"];

        foreach (var path in paths)
        {
            var page = await GetPageAsync(host, path);
            var tabs = page.QuerySelectorAll("nav[aria-label='Lists of records'] a").Select(tab => (tab.TextContent.Trim(), tab.GetAttribute("href"))).ToArray();
            Assert.Equal([("Votes", BaseUrl.Join("votes.html")), ("Bills", BaseUrl.Join("bills.html")), ("Statements", BaseUrl.Join("statements.html")),
                ("Executive actions", BaseUrl.Join("executive-actions.html"))], tabs);
            Assert.Equal(BaseUrl.Join(path[1..]), page.QuerySelector("nav[aria-label='Lists of records'] a[aria-current]")!.GetAttribute("href"));
            var header = page.QuerySelectorAll("nav.public-nav a").ToDictionary(link => link.TextContent.Trim(), link => link.GetAttribute("href"));
            Assert.Equal(BaseUrl.Join("votes.html"), header["Votes"]);
            Assert.Equal(BaseUrl.Join("bills.html"), header["Bills"]);
            Assert.Equal(BaseUrl.Join("executive-actions.html"), header["Executive actions"]);
        }
    }

    [Fact]
    public async Task ANoReleaseAnswers503AndACursorOfAnotherReleaseRedirectsToTheFirstPage()
    {
        await using var host = await ApiHost.StartAsync(postgres, options => options with { PublicBaseUrl = BaseUrl });
        foreach (var path in new[] { "/votes.html", "/bills.html", "/statements.html", "/executive-actions.html" })
            await GetPageAsync(host, path, HttpStatusCode.ServiceUnavailable);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);

        var stale = new VoteCursor(99, new DateOnly(2026, 3, 1), PublishedChamber.House, 1, 1, new string('a', 64)).Encode();
        using var noRedirect = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = host.Client.BaseAddress };
        var response = await noRedirect.GetAsync($"/votes.html?chamber=house&cursor={stale}");
        var before = await noRedirect.GetAsync($"/votes.html?chamber=house&before={stale}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(BaseUrl.Join("votes.html?chamber=house"), response.Headers.Location!.OriginalString);
        // A previous page cursor of another release behaves as a next page cursor does.
        Assert.Equal(HttpStatusCode.Redirect, before.StatusCode);
        Assert.Equal(BaseUrl.Join("votes.html?chamber=house"), before.Headers.Location!.OriginalString);
    }

    // A malformed previous page cursor, or a request with both cursors, gives the refused page with no rows, as a malformed next page cursor does.
    [Fact]
    public async Task AMalformedPreviousPageCursorAnswersTheRefusedPageAsAMalformedNextPageCursorDoes()
    {
        await using var host = await StartAsync();
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote('a', "2026-03-02", 1));
        var valid = new VoteCursor(1, new DateOnly(2026, 3, 1), PublishedChamber.House, 1, 1, new string('a', 64)).Encode();

        foreach (var path in new[] { "/votes.html?cursor=v.1.x", "/votes.html?before=v.1.x", $"/votes.html?cursor={valid}&before={valid}",
                     "/bills.html?before=bl.1", "/statements.html?before=t", "/executive-actions.html?before=x.1.2" })
        {
            var page = await GetPageAsync(host, path, HttpStatusCode.BadRequest);
            Assert.Empty(Rows(page));
            Assert.Null(page.QuerySelector(".pager"));
        }
    }

    [Fact]
    public async Task ARefusedFilterAnswers400WithThePageTheTypedTextAndTheRefusedFieldMarkedAndNoRows()
    {
        await using var host = await StartAsync();
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote('a', "2026-03-02", 1));
        await BillsSeed.AddBillsAsync(host.OperationalDatabase, new SeededBill('a', "2026-03-02"));

        var bill = await GetPageAsync(host, "/bills.html?bill=5334a&sponsor=Ann", HttpStatusCode.BadRequest);
        var conflict = await GetPageAsync(host, "/bills.html?type=s&bill=H.R.%201002", HttpStatusCode.BadRequest);
        var date = await GetPageAsync(host, "/votes.html?from=2026-13-45&chamber=house", HttpStatusCode.BadRequest);
        var order = await GetPageAsync(host, "/statements.html?from=2026-02-01&to=2026-01-01", HttpStatusCode.BadRequest);
        var voteOrder = await GetPageAsync(host, "/votes.html?from=2026-03-03&to=2026-03-01", HttpStatusCode.BadRequest);
        var choice = await GetPageAsync(host, "/executive-actions.html?type=law", HttpStatusCode.BadRequest);

        var number = bill.QuerySelector("input[name=bill]")!;
        Assert.Equal(("5334a", "true"), (number.GetAttribute("value"), number.GetAttribute("aria-invalid")));
        Assert.Equal("Enter a bill number, such as 5334 or H.R. 5334.", ErrorOf(bill, number));
        Assert.Equal("Ann", bill.QuerySelector("input[name=sponsor]")!.GetAttribute("value"));
        Assert.Null(bill.QuerySelector("input[name=sponsor]")!.GetAttribute("aria-invalid"));
        var type = conflict.QuerySelector("select[name=type]")!;
        Assert.Equal("true", type.GetAttribute("aria-invalid"));
        Assert.Equal("The bill number and the bill type name two different types.", ErrorOf(conflict, type));
        var from = date.QuerySelector("input[name=from]")!;
        Assert.Equal("true", from.GetAttribute("aria-invalid"));
        Assert.Equal("Enter the date as year, month and day, for example 2026-09-01.", ErrorOf(date, from));
        Assert.Equal("house", date.QuerySelector("select[name=chamber] option[selected]")!.GetAttribute("value"));
        var orderFrom = order.QuerySelector("input[name=from]")!;
        Assert.Equal("true", orderFrom.GetAttribute("aria-invalid"));
        Assert.Equal("The To date must be on or after the From date.", ErrorOf(order, orderFrom));
        Assert.Equal("The To date must be on or after the From date.", ErrorOf(voteOrder, voteOrder.QuerySelector("input[name=from]")!));
        var choiceType = choice.QuerySelector("select[name=type]")!;
        Assert.Equal(("true", null), (choiceType.GetAttribute("aria-invalid"), choiceType.GetAttribute("aria-describedby")));
        foreach (var page in new[] { bill, conflict, date, order, voteOrder, choice })
        {
            Assert.NotNull(page.QuerySelector(".list-fold[open] form"));
            Assert.Empty(Rows(page));
            Assert.Null(page.QuerySelector(".list-status"));
            Assert.Null(page.QuerySelector(".empty"));
            Assert.Equal(1, page.QuerySelectorAll("[aria-invalid]").Length);
        }
        foreach (var page in new[] { bill, conflict, date, order })
            Assert.Single(page.QuerySelectorAll(".field-error"));
        Assert.Empty(choice.QuerySelectorAll(".field-error"));
    }

    // The error text that the field names through aria-describedby.
    private static string ErrorOf(IDocument page, IElement field) => page.GetElementById(field.GetAttribute("aria-describedby")!)!.TextContent;

    [Fact]
    public async Task AReleaseWithNoRecordsShowsItsOneLineAndNoTotalNoFiltersAndNoNoMatchTips()
    {
        await using var host = await StartAsync();

        foreach (var (path, line) in new[]
                 {
                     ("/votes.html", "No votes are published yet."), ("/bills.html", "No bills or resolutions are published yet."),
                     ("/statements.html", "No statements are published yet."), ("/executive-actions.html", "No executive actions are published yet.")
                 })
        {
            var page = await GetPageAsync(host, path);
            Assert.DoesNotContain("in this release", Lede(page));
            Assert.Null(page.QuerySelector(".list-fold"));
            Assert.Null(page.QuerySelector(".list-status"));
            Assert.Equal(line, Assert.Single(page.QuerySelectorAll(".empty")).TextContent.Trim());
            Assert.Equal(line, page.QuerySelector(".empty > h2")!.TextContent);
            Assert.Empty(page.QuerySelectorAll(".search-next"));
            Assert.Empty(Rows(page));
        }
    }

    [Fact]
    public async Task TheFilterFoldOpensOnAWideScreenByScriptAndTheNoMatchHeadingFollowsThePageTitle()
    {
        await using var host = await StartAsync();
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote('a', "2026-03-02", 1));

        var page = await GetPageAsync(host, "/votes.html");
        var empty = await GetPageAsync(host, "/votes.html?chamber=senate");
        var script = File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "src", "CivicLens.PublicWeb", "wwwroot", "js", "public.js"));

        Assert.NotNull(page.QuerySelector("details.list-fold[data-wide-open]:not([open])"));
        Assert.Contains("if (matchMedia(\"(min-width: 720px)\").matches) document.querySelectorAll(\"details[data-wide-open]\")", script, StringComparison.Ordinal);
        Assert.Equal("No vote matches these filters.", empty.QuerySelector(".empty > h2")!.TextContent);
        Assert.Null(empty.QuerySelector(".empty h3"));
    }

    // ---- votes ----

    private static SeededVote Vote(char id, string day, int rollCall) => new(id, day) { RollCall = rollCall, TextOrigin = "Person", Casts = ["Yea", "Yea", "Nay"] };

    [Fact]
    public async Task TheVoteListShowsNewestFirstRowsWithCountsAndTheAiLabelOnItsOwnLineOnlyWhenTheViewHasOne()
    {
        await using var host = await StartAsync();
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, new SeededVote('a', "2026-03-02") { TextOrigin = "Model" }, Vote('b', "2026-03-03", 2));

        var response = await host.Client.GetAsync("/votes.html");
        var page = await GetPageAsync(host, "/votes.html");

        Assert.Equal(PageCache.CacheControl, string.Join(", ", response.Headers.GetValues("Cache-Control")));
        Assert.Equal(["Headline b", "Headline a"], Titles(page));
        var (person, model) = (Rows(page)[0], Rows(page)[1]);
        Assert.Null(Label(person));
        Assert.Equal(ModelLabel, Label(model));
        Assert.Equal("Headline a", model.QuerySelector("a > span")!.TextContent);
        AiLabelAssert.IsChip(model.QuerySelector("a + .ai-label")!, AiLabelWords.ModelChip, ModelLabel);
        Assert.Contains("Voted on 2 Mar 2026, House roll call 1, Vote to pass or agree to, H.R. 1", Text(model));
        Assert.Contains("2 Yes", Text(model));
        Assert.Equal("Showing 2 votes on this page.", page.QuerySelector(".list-status")!.TextContent);
        Assert.Empty(Chips(page));
        Assert.Equal("2 votes are in this release. House and Senate roll call votes, newest first. To see how one member voted, open their page from Officials.", Lede(page));
        var officials = page.QuerySelector(".page-header-lede a")!;
        Assert.Equal(("Officials", "https://civic-lens.test/officials.html"), (officials.TextContent, officials.GetAttribute("href")));
        Assert.Null(page.QuerySelector(".page-header-meta"));
        Assert.Equal("model", model.QuerySelector(".ai-label")!.GetAttribute("data-origin"));
    }

    [Fact]
    public async Task TheVoteListFiltersByChamberKindBillNumberAndDateAndKeepsTheFiltersInTheForm()
    {
        await using var host = await StartAsync();
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase,
            Vote('a', "2026-03-01", 1),
            Vote('b', "2026-03-02", 2) with { Chamber = "Senate", Kind = "Cloture", BillType = "s", BillNumber = 5 },
            Vote('c', "2026-03-03", 3) with { Kind = "Amendment" });

        Assert.Equal(["Headline b"], Titles(await GetPageAsync(host, "/votes.html?chamber=senate")));
        Assert.Equal(["Headline b"], Titles(await GetPageAsync(host, "/votes.html?kind=cloture")));
        Assert.Equal(["Headline b"], Titles(await GetPageAsync(host, "/votes.html?bill=S.%205")));
        Assert.Equal(["Headline c"], Titles(await GetPageAsync(host, "/votes.html?kind=amendment&from=2026-03-03")));
        Assert.Equal(["Headline c", "Headline a"], Titles(await GetPageAsync(host, "/votes.html?bill=1")));
        var page = await GetPageAsync(host, "/votes.html?chamber=senate&from=2026-03-01");
        Assert.Equal("Showing 1 vote on this page.", page.QuerySelector(".list-status")!.TextContent);
        Assert.Equal(["Senate", "On or after 1 Mar 2026"], Chips(page));
        Assert.StartsWith("3 votes are in this release.", Lede(page));
        Assert.Equal(["Bill S. 5", "From 1 Mar 2026 to 2 Mar 2026"], Chips(await GetPageAsync(host, "/votes.html?bill=S.%205&from=2026-03-01&to=2026-03-02")));
        Assert.Equal(["Vote to end debate", "On or before 9 Mar 2026"], Chips(await GetPageAsync(host, "/votes.html?kind=cloture&to=2026-03-09")));
        Assert.Equal("senate", page.QuerySelector("select[name=chamber] option[selected]")!.GetAttribute("value"));
        Assert.Equal("2026-03-01", page.QuerySelector("input[name=from]")!.GetAttribute("value"));
        // The fold closes after Apply on every screen, and the chips name the active filters.
        Assert.NotNull(page.QuerySelector("details.list-fold:not([open]):not([data-wide-open])"));
        Assert.NotNull(page.QuerySelector("a[href='?']"));
    }

    [Fact]
    public async Task TheVoteListPagesByTwentyFiveKeepsItsFiltersAndTheEmptyStateOffersNextSteps()
    {
        await using var host = await StartAsync();
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, [.. Enumerable.Range(1, 26).Select(index =>
            Vote((char)('A' + index), $"2026-03-{index:00}", index) with { RecordId = new string((char)('a' + index % 6), 60) + index.ToString("0000") })]);

        var first = await GetPageAsync(host, "/votes.html?chamber=house");
        var next = first.QuerySelector(".pager a[rel=next]")!.GetAttribute("href")!;
        var second = await GetPageAsync(host, "/votes.html" + next);

        Assert.Equal(VoteListReader.PageSize, Rows(first).Length);
        Assert.StartsWith("?chamber=house&cursor=", next);
        Assert.Single(Rows(second));
        // The last page has no next link, and its previous link gives the first page again (#580 item 7).
        Assert.Equal(["Next page"], PagerLinks(first));
        Assert.Equal(["Previous page"], PagerLinks(second));
        var previous = second.QuerySelector(".pager a[rel=prev]")!.GetAttribute("href")!;
        Assert.StartsWith("?chamber=house&before=", previous);
        var back = await GetPageAsync(host, "/votes.html" + previous);
        Assert.Equal(Titles(first), Titles(back));
        Assert.Equal(["Next page"], PagerLinks(back));
        Assert.Equal(next, back.QuerySelector(".pager a[rel=next]")!.GetAttribute("href"));
        Assert.Empty(Titles(first).Intersect(Titles(second)));
        var empty = await GetPageAsync(host, "/votes.html?chamber=senate");
        Assert.Contains("No vote matches these filters.", empty.QuerySelector(".empty")!.TextContent);
        Assert.Empty(Rows(empty));
        Assert.Equal("Showing 0 votes on this page.", empty.QuerySelector(".list-status")!.TextContent);
        Assert.Equal(["Senate"], Chips(empty));
    }

    // The words of every link and button of the pager, so a disabled button that promises no page also counts.
    private static string[] PagerLinks(IDocument page) => [.. page.QuerySelectorAll(".pager a, .pager button").Select(link => link.TextContent.Trim())];

    // ---- bills ----

    private static string BillId(char id) => new(id, 64);

    // The bill list reads its Congress and total from the stored counts of the release, so each seed stores them again.
    private static async Task AddBillsAsync(ApiHost host, params SeededBill[] bills)
    {
        await BillsSeed.AddBillsAsync(host.OperationalDatabase, bills);
        await StoreBillCountsAsync(host);
    }

    private static async Task StoreBillCountsAsync(ApiHost host)
    {
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "DELETE FROM release_official_bill_counts; DELETE FROM release_bill_counts; DELETE FROM release_official_record_counts;");
        await StoredBillCounts.FillAsync(host.OperationalDatabase, 1, 119);
    }

    private static string StoredStatus(char id, string text, string? status) => PublicRecordPageRows.PinnedStatus(1, BillId(id), text, "2026-02-13", PublishedAt.UtcTicks)
        + (status is null ? "" : $"\nUPDATE release_bill_statuses SET status = '{status}' WHERE record_id = '{BillId(id)}';");

    [Fact]
    public async Task TheBillListShowsSponsorLatestActionTheStoredStatusBadgeAndTheAiLabelOnlyWhenTheViewHasOne()
    {
        await using var host = await StartAsync();
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase, new SeededOfficial("p1", "Ann Able"));
        await AddBillsAsync(host,
            new SeededBill('a', "2026-03-02") { Sponsorships = [new("p1", "Sponsor", "2026-03-02")] },
            new SeededBill('b', "2026-03-03") { TextOrigin = "Person", BillNumber = 8, Sponsorships = [new("p2", "Sponsor", "2026-03-03")] },
            new SeededBill('c', "2026-03-04") { BillNumber = 9 });
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;\n"
            + StoredStatus('a', "Referred to the House Committee on Energy and Commerce.", "InCommittee") + "\n"
            + StoredStatus('c', "Placed on the Union Calendar, Calendar No. 12.", "Other"));

        var page = await GetPageAsync(host, "/bills.html");

        // A and C share the latest action date, so the record ID orders them. B has no stored status, so it comes last.
        var (model, other, person) = (Rows(page)[0], Rows(page)[1], Rows(page)[2]);
        Assert.Null(Label(person));
        Assert.Equal(ModelLabel, Label(model));
        Assert.Equal("model", model.QuerySelector(".ai-label")!.GetAttribute("data-origin"));
        Assert.DoesNotContain("Sponsor:", Text(person));
        Assert.Contains("Sponsor: Ann Able", Text(model));
        Assert.All(Rows(page), row => Assert.DoesNotContain("sponsorship", Text(row), StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Latest official action 13 Feb 2026: In committee: sent to the House Committee on Energy and Commerce.", Text(model));
        Assert.Equal("In committee", model.QuerySelector(".badge")!.TextContent.Trim());
        Assert.Null(person.QuerySelector(".badge"));
        Assert.Contains("Waiting for a possible House floor vote.", Text(other));
        Assert.Null(other.QuerySelector(".badge"));
        Assert.Equal("Showing 3 bills and resolutions on this page.", page.QuerySelector(".list-status")!.TextContent);
        Assert.Equal(["In committee", "Passed the House", "Passed the Senate", "Became law", "Other"],
            page.QuerySelectorAll("select[name=status] option").Skip(1).Select(option => option.TextContent));
    }

    [Fact]
    public async Task TheBillListCountsAndNamesTheNewestCongressAndOffersOnlyThePolicyAreasOfThatCongress()
    {
        await using var host = await StartAsync();
        await AddBillsAsync(host, new SeededBill('a', "2026-03-02") { PolicyAreaId = "tax" },
            new SeededBill('c', "2026-03-03") { BillNumber = 9, PolicyAreaId = "tax" },
            new SeededBill('b', "2024-03-02") { Congress = 118, BillNumber = 8, PolicyAreaId = "energy" });

        var page = await GetPageAsync(host, "/bills.html");
        var filtered = await GetPageAsync(host, "/bills.html?policyArea=tax");

        Assert.Equal(["a", "c"], Ids(page));
        Assert.Equal("2 bills and resolutions of the 119th Congress are in this release. "
            + "Bills and resolutions in the 119th Congress, with the latest official action on each. The newest action comes first.", Lede(page));
        Assert.Equal(Lede(page), Lede(filtered));
        Assert.Equal(["tax"], page.QuerySelectorAll("select[name=policyArea] option").Skip(1).Select(option => option.GetAttribute("value")));
    }

    [Fact]
    public async Task TheBillListTakesTheNewestCongressAndItsTotalFromTheStoredCountsAndGivesEachBillItsOwnSponsorAndStatus()
    {
        await using var host = await StartAsync();
        await using var unstoredHost = await StartAsync();
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase, new SeededOfficial("p1", "Ann Able"), new SeededOfficial("p2", "Bo Zed"));
        await AddBillsAsync(host,
            new SeededBill('a', "2026-03-02") { Sponsorships = [new("p1", "Sponsor", "2026-03-02")] },
            new SeededBill('b', "2026-03-04") { BillNumber = 8, Sponsorships = [new("p2", "Sponsor", "2026-03-04"), new("p1", "Cosponsor", "2026-03-04")] },
            new SeededBill('c', "2026-03-03") { BillNumber = 9, Sponsorships = [new("p1", "Sponsor", "2026-03-03")] },
            new SeededBill('d', "2024-05-01") { Congress = 118, BillNumber = 10, Sponsorships = [new("p2", "Sponsor", "2024-05-01")] });
        await BillsSeed.AddBillsAsync(unstoredHost.OperationalDatabase, new SeededBill('a', "2026-03-02"));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;\n"
            + StoredStatus('a', "Referred to the House Committee on Energy and Commerce.", "InCommittee") + "\n"
            + StoredStatus('b', "Received in the Senate.", "PassedHouse") + "\n"
            + StoredStatus('d', "Became Public Law No: 118-1.", "BecameLaw"));

        var page = await GetPageAsync(host, "/bills.html");

        Assert.Equal(["a", "b", "c"], Ids(page));
        Assert.StartsWith("3 bills and resolutions of the 119th Congress are in this release.", Lede(page), StringComparison.Ordinal);
        var (committee, passed, unknown) = (Rows(page)[0], Rows(page)[1], Rows(page)[2]);
        Assert.Contains("Sponsor: Bo Zed", Text(passed));
        Assert.Equal("Passed the House", passed.QuerySelector(".badge")!.TextContent.Trim());
        Assert.Contains("Sponsor: Ann Able", Text(unknown));
        Assert.Null(unknown.QuerySelector(".badge"));
        Assert.Contains("Sponsor: Ann Able", Text(committee));
        Assert.Equal("In committee", committee.QuerySelector(".badge")!.TextContent.Trim());
        Assert.Equal(["b"], Ids(await GetPageAsync(host, "/bills.html?status=passedHouse")));
        Assert.Equal(["a", "c"], Ids(await GetPageAsync(host, "/bills.html?sponsor=ann")));
        Assert.Empty(Ids(await GetPageAsync(host, "/bills.html?status=becameLaw")));

        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "UPDATE release_bill_counts SET bills = 40 WHERE congress = 119;");
        Assert.StartsWith("40 bills and resolutions of the 119th Congress", Lede(await GetPageAsync(host, "/bills.html")), StringComparison.Ordinal);

        var unstored = await GetPageAsync(unstoredHost, "/bills.html");
        Assert.Equal("Bills and resolutions, with the latest official action on each. The newest action comes first.", Lede(unstored));
        Assert.Empty(Rows(unstored));
    }

    [Fact]
    public async Task ABillListOfOneBillUsesTheSingularTotalAndAReleaseWithNoBillNamesNoCongress()
    {
        await using var emptyHost = await StartAsync();
        await using var host = await StartAsync();
        await StoreBillCountsAsync(emptyHost);
        await AddBillsAsync(host, new SeededBill('a', "2026-03-02"));

        var empty = await GetPageAsync(emptyHost, "/bills.html");
        var one = await GetPageAsync(host, "/bills.html");

        Assert.Equal("Bills and resolutions, with the latest official action on each. The newest action comes first.", Lede(empty));
        Assert.StartsWith("1 bill or resolution of the 119th Congress is in this release. Bills and resolutions in the 119th Congress", Lede(one), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AVoteOrBillListFilteredByAKnownIssueTakesTheHeadingOfTheIssuePageSectionAndAnyOtherListKeepsItsLabel()
    {
        await using var host = await StartAsync();
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, new SeededVote('a', "2026-03-02") { Issues = ["health"] }, Vote('b', "2026-03-03", 2));
        await AddBillsAsync(host, new SeededBill('c', "2026-03-01") { Issues = ["health"], BillNumber = 1 }, new SeededBill('d', "2026-03-02") { BillNumber = 2 });
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;\n"
            + "INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (1, 'health', 'Health care', 'Defined');");

        var votes = await GetPageAsync(host, "/votes.html?issue=health");
        var bills = await GetPageAsync(host, "/bills.html?issue=health");

        Assert.Equal(IssueRecordWords.VotesHeading("Health care"), votes.QuerySelector("h1")!.TextContent);
        Assert.Equal(["Headline a"], Titles(votes));
        Assert.StartsWith("Roll call votes on Health care", votes.Title);
        Assert.Equal((IssueRecordWords.BillsHeading("Health care"), 1), (bills.QuerySelector("h1")!.TextContent, Rows(bills).Length));
        Assert.StartsWith("Bills tagged Health care", bills.Title);
        Assert.Equal("Votes", (await GetPageAsync(host, "/votes.html")).QuerySelector("h1")!.TextContent);
        Assert.Equal("Votes", (await GetPageAsync(host, "/votes.html?issue=nothing")).QuerySelector("h1")!.TextContent);
        Assert.Equal("Bills", (await GetPageAsync(host, "/bills.html?issue=nothing")).QuerySelector("h1")!.TextContent);
    }

    [Fact]
    public async Task TheBillListFiltersByTypeStatusSponsorNameIssuePolicyAreaBillNumberAndDateAndPagesByTwentyFive()
    {
        await using var host = await StartAsync();
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase, new SeededOfficial("p1", "Ann Able"), new SeededOfficial("p2", "Bo Zed"));
        await AddBillsAsync(host,
            new SeededBill('a', "2026-03-01") { Issues = ["health"], BillNumber = 4, Sponsorships = [new("p1", "Sponsor", "2026-03-01"), new("p2", "Cosponsor", "2026-03-01")] },
            new SeededBill('b', "2026-03-02") { BillType = "s", BillNumber = 4, PolicyAreaId = "tax", Sponsorships = [new("p2", "Sponsor", "2026-03-02"), new("p1", "Cosponsor", "2026-03-02")] },
            new SeededBill('c', "2026-03-03") { BillNumber = 9, Sponsorships = [new("p3", "Sponsor", "2026-03-03")] });
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;\n"
            + StoredStatus('a', "Received in the Senate.", "PassedHouse") + "\n" + StoredStatus('c', "Became Public Law No: 119-1.", null) + "\n"
            + "INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (1, 'health', 'Health care', 'Defined');");

        Assert.Equal(["a"], Ids(await GetPageAsync(host, "/bills.html?issue=health")));
        Assert.Equal(["b"], Ids(await GetPageAsync(host, "/bills.html?policyArea=tax")));
        Assert.Equal(["b"], Ids(await GetPageAsync(host, "/bills.html?bill=S.%204")));
        Assert.Equal(["b"], Ids(await GetPageAsync(host, "/bills.html?type=s")));
        Assert.Equal(["a", "b"], Ids(await GetPageAsync(host, "/bills.html?bill=4")).Order());
        Assert.Equal(["a"], Ids(await GetPageAsync(host, "/bills.html?bill=4&type=hr")));
        Assert.Equal(["a", "c"], Ids(await GetPageAsync(host, "/bills.html?to=2026-02-13")));
        Assert.Empty(Ids(await GetPageAsync(host, "/bills.html?from=2026-03-03")));
        Assert.Equal(["a"], Ids(await GetPageAsync(host, "/bills.html?status=passedHouse")));
        Assert.Empty(Ids(await GetPageAsync(host, "/bills.html?status=becameLaw")));
        Assert.Equal(["a"], Ids(await GetPageAsync(host, "/bills.html?sponsor=ab")));
        Assert.Equal(["b"], Ids(await GetPageAsync(host, "/bills.html?sponsor=ZED")));
        await GetPageAsync(host, "/bills.html?type=law", HttpStatusCode.BadRequest);
        await GetPageAsync(host, "/bills.html?status=passed", HttpStatusCode.BadRequest);
        var filtered = await GetPageAsync(host, "/bills.html?type=s&sponsor=Bo&policyArea=tax&bill=4&status=passedHouse&issue=health&to=2026-03-09");
        var taxArea = filtered.QuerySelector("select[name=policyArea] option[value=tax]")!.TextContent;
        Assert.Equal(["S. (Senate bill)", "Number S. 4", "Sponsor: Bo", "Passed the House", "Health care", taxArea, "On or before 9 Mar 2026"], Chips(filtered));
        Assert.Equal("tax", filtered.QuerySelector("select[name=policyArea] option[selected]")!.GetAttribute("value"));
        Assert.Equal("Bo", filtered.QuerySelector("input[name=sponsor]")!.GetAttribute("value"));
        Assert.Contains(filtered.QuerySelectorAll("select[name=policyArea] option").Select(option => option.GetAttribute("value")), value => value == "health");
        Assert.Contains("No bill matches these filters.", (await GetPageAsync(host, "/bills.html?issue=nothing")).QuerySelector(".empty")!.TextContent);

        await AddBillsAsync(host, [.. Enumerable.Range(0, 25).Select(index =>
            new SeededBill((char)('d' + index % 20), $"2026-02-{index + 1:00}") { RecordId = new string('d', 60) + index.ToString("0000"), BillNumber = 100 + index })]);
        var first = await GetPageAsync(host, "/bills.html");
        var next = first.QuerySelector(".pager a[rel=next]")!.GetAttribute("href")!;
        var second = await GetPageAsync(host, "/bills.html" + next);
        Assert.Equal(BillListReader.PageSize, Rows(first).Length);
        Assert.Equal(3, Rows(second).Length);
        Assert.Empty(Titles(first).Intersect(Titles(second)));
    }

    // Bill a has its sponsorship in February and its latest action in March. Bill b has the reverse. Bill c has no stored status. Bill d has both in March.
    [Theory]
    [InlineData("from=2026-03-15&to=2026-03-31", "ad")]
    [InlineData("from=2026-02-01&to=2026-02-28", "b")]
    [InlineData("to=2026-03-16", "db")]
    public async Task TheBillListPageAndTheJsonListFilterTheLatestActionDateAndGiveTheSameBillsInTheSameOrder(string query, string expected)
    {
        await using var host = await StartAsync();
        await AddBillsAsync(host,
            new SeededBill('a', "2026-02-02") { BillNumber = 1, Sponsorships = [new("p1", "Sponsor", "2026-02-02")] },
            new SeededBill('b', "2026-03-20") { BillNumber = 2, Sponsorships = [new("p1", "Sponsor", "2026-03-20")] },
            new SeededBill('c', "2026-03-20") { BillNumber = 3, Sponsorships = [new("p1", "Sponsor", "2026-03-20")] },
            new SeededBill('d', "2026-03-16") { BillNumber = 4, Sponsorships = [new("p1", "Sponsor", "2026-03-16")] });
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;\n"
            + PublicRecordPageRows.PinnedStatus(1, BillId('a'), "Referred.", "2026-03-20", PublishedAt.UtcTicks, "InCommittee") + "\n"
            + PublicRecordPageRows.PinnedStatus(1, BillId('b'), "Referred.", "2026-02-02", PublishedAt.UtcTicks, "InCommittee") + "\n"
            + PublicRecordPageRows.PinnedStatus(1, BillId('d'), "Referred.", "2026-03-16", PublishedAt.UtcTicks, "InCommittee"));

        var page = Ids(await GetPageAsync(host, "/bills.html?" + query));
        using var response = await host.Client.GetAsync("/v1/bills?" + query);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var listed = json.RootElement.GetProperty("bills").EnumerateArray().Select(bill => bill.GetProperty("recordId").GetString()![..1]).ToArray();

        Assert.Equal([.. expected.Select(id => id.ToString())], page);
        Assert.Equal(page, listed);
    }

    [Fact]
    public async Task ACursorOfTheEarlierSponsorshipSortAnswersTheRefusedListPage()
    {
        await using var host = await StartAsync();
        await AddBillsAsync(host, new SeededBill('a', "2026-03-02"));

        var page = await GetPageAsync(host, $"/bills.html?status=inCommittee&cursor=b.1.20260302.{new string('a', 64)}", HttpStatusCode.BadRequest);

        Assert.Empty(Rows(page));
        Assert.Null(page.QuerySelector(".pager a[rel=next]"));
        Assert.Equal("inCommittee", page.QuerySelector("select[name=status] option[selected]")!.GetAttribute("value"));
    }

    // The list once joined each filter as a semi join to a view. The oracle keeps that form, so the test proves that the keyed read gives the same bills.
    // The oracle orders them by the latest action of bill_status, the view of the status badge, so it also proves the order of the keys.
    // The date filters of the oracle test that latest action too, so they prove the date that the From and To filters test.
    private static string OracleFilter(string name, string value) => name switch
    {
        "status" => $"EXISTS (SELECT 1 FROM public_api.bill_status t WHERE t.record_id = b.record_id AND t.revision_number = b.revision_number AND t.status = '{char.ToUpperInvariant(value[0])}{value[1..]}')",
        "issue" => $"EXISTS (SELECT 1 FROM public_api.bill_issues i WHERE i.record_id = b.record_id AND i.revision_number = b.revision_number AND i.issue_id = '{value}')",
        "sponsor" => $"""
            EXISTS (SELECT 1 FROM public_api.bill_sponsorships s JOIN public_api.officials o ON o.official_id = s.official_id
                     WHERE s.record_id = b.record_id AND s.revision_number = b.revision_number AND s.role = 'Sponsor'
                       AND strpos(' ' || lower(o.name), ' ' || lower('{value}')) > 0)
            """,
        "type" => $"b.bill_type = '{value}'",
        "policyArea" => $"b.policy_area_id = '{value}'",
        "from" => $"t.latest_action_date >= '{value}'",
        "to" => $"t.latest_action_date <= '{value}'",
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
    };

    // Stage 4 has no status row and stage 3 has a row with no stage. An inactive release holds another stage and a newer action for each bill of stage 0.
    // The action date repeats every four bills and the event date every twelve, so the two orders differ and a page can end inside a tie.
    private static string StatusRows(SeededBill bill, int index)
    {
        var date = $"2026-03-{1 + index % 4:00}";
        return (index % 5) switch
        {
            4 => "",
            3 => PublicRecordPageRows.PinnedStatus(1, bill.RecordId, "Introduced.", date, PublishedAt.UtcTicks),
            var stage => PublicRecordPageRows.PinnedStatus(1, bill.RecordId, "Acted on.", date, PublishedAt.UtcTicks, Enum.GetName((BillStatusStage)stage))
                + (stage == 0 ? "\n" + PublicRecordPageRows.PinnedStatus(2, bill.RecordId, "Acted on.", "2026-04-01", PublishedAt.UtcTicks, nameof(BillStatusStage.PassedHouse)) : "")
        };
    }

    [Theory]
    [InlineData("")]
    [InlineData("status=inCommittee")]
    [InlineData("status=passedHouse")]
    [InlineData("status=passedSenate")]
    [InlineData("issue=health")]
    [InlineData("issue=energy")]
    [InlineData("sponsor=ann")]
    [InlineData("sponsor=zed")]
    [InlineData("type=s")]
    [InlineData("policyArea=tax")]
    [InlineData("from=2026-03-02&to=2026-03-03")]
    [InlineData("issue=health&status=inCommittee")]
    [InlineData("issue=health&sponsor=ann")]
    [InlineData("sponsor=bo&status=passedHouse")]
    [InlineData("type=s&status=passedSenate&policyArea=tax")]
    [InlineData("issue=energy&sponsor=zed&status=inCommittee&from=2026-03-02")]
    public async Task EachBillListFilterGivesTheBillsOfTheNewestCongressThatTheViewsMatchInListOrderOnEveryPage(string query)
    {
        await using var host = await StartAsync();
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase, new SeededOfficial("p0", "Ann Able"), new SeededOfficial("p1", "Bo Zed"), new SeededOfficial("p2", "Ann Zed"));
        // Three bills share each day, so the record ID orders them. Every ninth bill is of Congress 118, and some bills hold an untracked issue.
        var bills = Enumerable.Range(0, 36).Select(index => new SeededBill('b', $"2026-02-{1 + index % 12:00}")
        {
            RecordId = new string('b', 60) + index.ToString("0000"),
            Congress = index % 9 == 8 ? 118 : 119,
            BillType = index % 3 == 0 ? "s" : "hr",
            BillNumber = 100 + index,
            PolicyAreaId = index % 4 == 0 ? "tax" : "health",
            Sponsorships = [new($"p{index % 3}", "Sponsor", "2026-02-01"), new($"p{(index + 1) % 3}", "Cosponsor", "2026-02-01")],
            Issues = [.. new[] { index % 2 == 0 ? "health" : null, index % 5 == 0 ? "energy" : null }.OfType<string>()],
            UntrackedIssues = index % 2 == 1 && index % 3 == 0 ? ["health"] : []
        }).ToArray();
        await AddBillsAsync(host, bills);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;\n" + string.Join("\n", bills.Select(StatusRows))
            + "\nINSERT INTO release_issues (release_number, issue_id, name) VALUES (1, 'health', 'Health care'), (1, 'energy', 'Energy');");
        var filters = query.Length == 0 ? [] : query.Split('&').Select(pair => pair.Split('=')).Select(pair => OracleFilter(pair[0], pair[1]));
        var expected = await ViewsSeed.QueryAsPublicAsync(host.OperationalDatabase,
            $"""
            SELECT b.record_id FROM public_api.bills b
              LEFT JOIN public_api.bill_status t ON t.record_id = b.record_id AND t.revision_number = b.revision_number
             WHERE {string.Join(" AND ", filters.Prepend("b.congress = 119"))}
             ORDER BY t.latest_action_date DESC NULLS LAST, b.record_id
            """);

        var listed = new List<string>();
        for (var path = "/bills.html?" + query; path is not null;)
        {
            var page = await GetPageAsync(host, path);
            listed.AddRange(Rows(page).Select(row => row.QuerySelector("a")!.GetAttribute("href")!.Split('/').Last().Replace(".html", "")));
            path = page.QuerySelector(".pager a[rel=next]")?.GetAttribute("href") is { } next ? "/bills.html" + next : null;
        }

        Assert.NotEmpty(expected);
        Assert.Equal(expected, listed);
    }

    private static string[] Ids(IDocument page) =>
        [.. Rows(page).Select(row => row.QuerySelector("a")!.GetAttribute("href")!.Split('/').Last().Replace(".html", "")[..1])];

    // ---- statements ----

    private static async Task AddStatementsAsync(ApiHost host, params SeededStatement[] statements)
    {
        await StatementsSeed.AddAsync(host.OperationalDatabase, statements);
        await StatementsSeed.AddSourcesAsync(host.OperationalDatabase, [.. statements.Select(statement => (statement.RecordId, $"https://site-{statement.Id}.senate.gov/news/{statement.RecordId[..8]}"))]);
    }

    [Fact]
    public async Task TheStatementListShowsEachSpeakerWithSeatAndPartyOnTheStatementDateTheSourceSiteTheVoteLinkAndTheAiLabelOnlyWhenTheViewHasOne()
    {
        await using var host = await StartAsync();
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase,
            new SeededOfficial("official-a", "Ann Able",
                new SeededTerm("House", "Representative", "2023-01-03") { State = "MA", District = 4, Party = "Republican", EndsOn = "2025-01-02" },
                new SeededTerm("House", "Representative", "2025-01-03") { State = "MA", District = 5, Party = "Democrat" }),
            new SeededOfficial("official-z", "Zed Zane", new SeededTerm("Senate", "Senator", "2025-01-03") { State = "AZ", Party = "Republican" }));
        await AddStatementsAsync(host,
            new('a', "2026-03-02", ["official-z", "official-a"], [], "Analysis", "Code"),
            new('b', null, ["official-a"], [], "Person", "Person"),
            new('c', "2024-06-03", ["official-a"], [], "Person", "Person"));
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote('f', "2026-03-01", 1));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;\n"
            + PublicRecordPageRows.Link(new string('7', 64), new string('a', 64), new string('f', 64), "vote", "StatementAboutVote", "2026-03-02"));

        var page = await GetPageAsync(host, "/statements.html");

        Assert.Equal(["Headline a", "Headline c", "Headline b"], Titles(page));
        var (model, earlier, person) = (Rows(page)[0], Rows(page)[1], Rows(page)[2]);
        Assert.StartsWith("An AI summary", AiLabelAssert.FullText(model.QuerySelector(".ai-label")!));
        Assert.Equal(AiLabelWords.ModelChip, model.QuerySelector(".ai-label-chip-text")!.TextContent);
        Assert.Equal("model", model.QuerySelector(".ai-label")!.GetAttribute("data-origin"));
        Assert.Null(person.QuerySelector(".ai-label"));
        Assert.Contains("Zed Zane, Senator for AZ, Republican, Ann Able, Representative for MA, district 5, Democrat, Said on 2 Mar 2026, From site-a.senate.gov", Text(model));
        Assert.Contains("Ann Able, Representative for MA, district 4, Republican, Said on 3 Jun 2024, From site-c.senate.gov", Text(earlier));
        // An undated statement has no day, so no term gives its speaker a seat or a party.
        Assert.Contains("Ann Able, Date not stated, From site-b.senate.gov", Text(person));
        Assert.Equal("Linked to a vote", model.QuerySelector(".badge")!.TextContent.Trim());
        Assert.Null(person.QuerySelector(".badge"));
        Assert.Equal("Showing 3 statements on this page.", page.QuerySelector(".list-status")!.TextContent);
        Assert.StartsWith("3 statements are in this release. What members of Congress", Lede(page));
    }

    [Fact]
    public async Task TheStatementListFiltersByATypedOfficialNameIssueAndDateAndShowsEachFilterAsAChip()
    {
        await using var host = await StartAsync();
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase, new SeededOfficial("official-a", "Ann Able"), new SeededOfficial("official-z", "Zed Zane"));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (1, 'health', 'Health care', 'Defined');");
        await AddStatementsAsync(host,
            new('a', "2026-03-02", ["official-z"], ["health"], "Person", "Person"),
            new('b', "2026-03-05", ["official-a"], [], "Person", "Person"),
            new('c', null, ["official-a"], [], "Person", "Person"));

        Assert.Equal(["Headline b", "Headline c"], Titles(await GetPageAsync(host, "/statements.html?official=Ann")));
        Assert.Equal(["Headline b", "Headline c"], Titles(await GetPageAsync(host, "/statements.html?official=able")));
        Assert.Empty(Titles(await GetPageAsync(host, "/statements.html?official=nn")));
        Assert.Equal(["Headline a"], Titles(await GetPageAsync(host, "/statements.html?issue=health")));
        Assert.Equal(["Headline b"], Titles(await GetPageAsync(host, "/statements.html?from=2026-03-03")));
        var page = await GetPageAsync(host, "/statements.html?official=Zed&issue=health&from=2026-03-01&to=2026-03-02");
        Assert.Equal(["Headline a"], Titles(page));
        Assert.Equal(["Official: Zed", "Health care", "From 1 Mar 2026 to 2 Mar 2026"], Chips(page));
        Assert.Equal("Zed", page.QuerySelector("input[name=official]")!.GetAttribute("value"));
        Assert.Equal("Health care", page.QuerySelector("select[name=issue] option[value=health]")!.TextContent);
        Assert.Contains("No statement matches these filters.", (await GetPageAsync(host, "/statements.html?official=nobody")).QuerySelector(".empty")!.TextContent);
    }

    [Fact]
    public async Task TheStatementListPagesByTwentyFiveAcrossDatedAndUndatedStatements()
    {
        await using var host = await StartAsync();
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase, new SeededOfficial("official-a", "Ann Able"));
        await AddStatementsAsync(host, [.. Enumerable.Range(1, 26).Select(index =>
            new SeededStatement('a', index <= 24 ? $"2026-03-{index:00}" : null, ["official-a"], [], "Person", "Person") { RecordId = new string('a', 60) + index.ToString("0000") })]);

        var first = await GetPageAsync(host, "/statements.html");
        var second = await GetPageAsync(host, "/statements.html" + first.QuerySelector(".pager a[rel=next]")!.GetAttribute("href"));

        Assert.Equal(25, Rows(first).Length);
        Assert.Single(Rows(second));
        Assert.Contains("Date not stated", Text(Rows(second)[0]));
        Assert.Contains("Date not stated", Text(Rows(first)[24]));
    }

    // ---- executive actions ----

    private static async Task SeedExecutiveActionsAsync(ApiHost host, params (string Id, string Kind, string Title, string SignedOn)[] actions) =>
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;\n" + string.Join('\n', actions.Select(action => $"""
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES ('executiveAction', '{action.Id}', 1, 1, NULL);
            INSERT INTO executive_action_revisions (record_id, revision_number, revision_json, created_at_utc_ticks, author_kind, author_subject, code_version,
                source_id, document_number, kind, title, signed_on, published_on, federal_register_citation, president_name, person_id, document_url)
              VALUES ('{action.Id}', 1, 'x', 1, 'Code', 'civic-lens:code', '{new string('c', 40)}', 'fr', '{action.Id[^8..]}', '{action.Kind}', '{action.Title}', '{action.SignedOn}',
                      '{action.SignedOn}', '91 FR 1', 'Jane Roe', 'president', 'https://fr.example/1');
            """)));

    private static string ExecutiveId(int index) => new string('e', 56) + index.ToString("00000000");

    [Fact]
    public async Task TheExecutiveActionListShowsKindSignedDateAndTheReleaseTotalWithNoAiLabelBecauseTheViewHasNoLabelColumn()
    {
        await using var host = await StartAsync();
        await SeedExecutiveActionsAsync(host, (ExecutiveId(1), "ExecutiveOrder", "Order one", "2026-01-20"), (ExecutiveId(2), "Proclamation", "Proclamation two", "2026-02-02"));

        var page = await GetPageAsync(host, "/executive-actions.html");

        Assert.Equal(["Proclamation two", "Order one"], Titles(page));
        Assert.Contains("Proclamation, Signed 2 Feb 2026", Text(Rows(page)[0]));
        Assert.Contains("Executive order, Signed 20 Jan 2026", Text(Rows(page)[1]));
        Assert.All(Rows(page), row => Assert.Null(Label(row)));
        Assert.Equal("Showing 2 executive actions on this page.", page.QuerySelector(".list-status")!.TextContent);
        Assert.Equal("2 executive actions are in this release. Documents that the President signed and the Federal Register printed, newest first. "
            + "Civic Lens copies each title and date from the Federal Register.", Lede(page));
        Assert.Null(page.QuerySelector(".notice"));
    }

    [Fact]
    public async Task TheExecutiveActionListFiltersByTypeAndDateAndPagesByTwentyFive()
    {
        await using var host = await StartAsync();
        await SeedExecutiveActionsAsync(host, [(ExecutiveId(0), "Notice", "Notice zero", "2025-12-31"),
            .. Enumerable.Range(1, 25).Select(index => (ExecutiveId(index), "ExecutiveOrder", $"Order {index:00}", $"2026-01-{index:00}"))]);

        var notices = await GetPageAsync(host, "/executive-actions.html?type=notice");
        Assert.Equal(["Notice zero"], Titles(notices));
        Assert.Equal(["Presidential notices"], Chips(notices));
        Assert.StartsWith("26 executive actions are in this release.", Lede(notices));
        var typeChips = new List<string>();
        foreach (var type in new[] { "executiveOrder", "proclamation", "notice", "memorandum" })
            typeChips.AddRange(Chips(await GetPageAsync(host, $"/executive-actions.html?type={type}")));
        Assert.Equal(["Executive orders", "Proclamations", "Presidential notices", "Memorandums"], typeChips);
        Assert.Equal(["Order 25"], Titles(await GetPageAsync(host, "/executive-actions.html?type=executiveOrder&from=2026-01-25")));
        var first = await GetPageAsync(host, "/executive-actions.html");
        var next = first.QuerySelector(".pager a[rel=next]")!.GetAttribute("href")!;
        var second = await GetPageAsync(host, "/executive-actions.html" + next);
        Assert.Equal(25, Rows(first).Length);
        Assert.Equal(["Notice zero"], Titles(second));
        Assert.Contains("Memorandum", (await GetPageAsync(host, "/executive-actions.html")).QuerySelectorAll("select[name=type] option").Select(option => option.TextContent));
        Assert.Contains("No executive action matches these filters.", (await GetPageAsync(host, "/executive-actions.html?type=memorandum")).QuerySelector(".empty")!.TextContent);
    }

    // Seven actions, five of them signed on one day, so a page can end inside the tie and the record ID orders it.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task EachPreviousPageOfTheExecutiveActionListGivesThePageBeforeItAgain(int limit)
    {
        await using var host = await StartAsync();
        await SeedExecutiveActionsAsync(host, [.. Enumerable.Range(1, 7).Select(index => (ExecutiveId(index), "ExecutiveOrder", $"Order {index}", $"2026-01-{Math.Min(index, 3):00}"))]);
        await using var views = Npgsql.NpgsqlDataSource.Create(host.Options.PublicViewsDatabase);
        var store = new ExecutiveActionStore(views);

        var seen = await KeysetWalk.BothWaysAsync(async (cursor, backward) =>
        {
            var page = (await store.ListAsync(new ExecutiveActionListRequest { Limit = limit, Cursor = ExecutiveActionCursor.Parse(cursor), Backward = backward }, CancellationToken.None))!;
            return new([.. page.Actions.Select(action => action.RecordId)], page.NextCursor, page.PreviousCursor);
        });

        Assert.Equal([3, 4, 5, 6, 7, 2, 1], seen.Select(id => int.Parse(id[^8..], System.Globalization.CultureInfo.InvariantCulture)));
    }
}
