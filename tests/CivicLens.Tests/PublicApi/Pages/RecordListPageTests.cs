using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicApi;
using CivicLens.PublicApi.Bills;
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

namespace CivicLens.Tests.PublicApi.Pages;

/// <summary>The gated per-kind list pages under /web: votes, bills, statements, and executive actions.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class RecordListPageTests(PostgresCollection postgres)
{
    private static readonly PublicBaseUrl BaseUrl = PublicBaseUrl.Parse("https://civic-lens.test", "test")!;
    private static readonly DateTimeOffset PublishedAt = new(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);
    private const string ModelLabel = "An AI summary of the official record";
    private const string CodeLabel = "Written automatically from Congress.gov data, with no AI";

    private async Task<ApiHost> StartAsync()
    {
        var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite(), options => options with { PublicBaseUrl = BaseUrl });
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

    private static string? Label(IElement row) => row.QuerySelectorAll("a > span").Select(span => span.TextContent).SingleOrDefault(text => text is ModelLabel or CodeLabel);

    private static string Text(IElement element) => element.TextContent;

    private static string[] Titles(IDocument page) => [.. Rows(page).Select(row => row.QuerySelector("a > span")!.TextContent)];

    private static string[] Chips(IDocument page) => [.. page.QuerySelectorAll("ul[aria-label='Active filters'] .badge").Select(chip => chip.TextContent)];

    private static string Lede(IDocument page) => page.QuerySelector(".page-header-lede")!.TextContent;

    // ---- shared frame: tabs, header, count line ----

    [Fact]
    public async Task EveryListPageNamesItsKindInTheTabsAndTheHeaderLinksOfThePreviewLeadToTheLists()
    {
        await using var host = await StartAsync();
        string[] paths = ["/web/votes", "/web/bills", "/web/statements", "/web/executive-actions"];

        foreach (var path in paths)
        {
            var page = await GetPageAsync(host, path);
            var tabs = page.QuerySelectorAll("nav[aria-label='Lists of records'] a").Select(tab => (tab.TextContent.Trim(), tab.GetAttribute("href"))).ToArray();
            Assert.Equal([("Votes", "https://civic-lens.test/web/votes"), ("Bills", "https://civic-lens.test/web/bills"), ("Statements", "https://civic-lens.test/web/statements"),
                ("Executive actions", "https://civic-lens.test/web/executive-actions")], tabs);
            Assert.Equal(path, "/" + new Uri(page.QuerySelector("nav[aria-label='Lists of records'] a[aria-current]")!.GetAttribute("href")!).AbsolutePath.TrimStart('/'));
            var header = page.QuerySelectorAll("nav.public-nav a").ToDictionary(link => link.TextContent.Trim(), link => link.GetAttribute("href"));
            Assert.Equal("https://civic-lens.test/web/votes", header["Votes"]);
            Assert.Equal("https://civic-lens.test/web/bills", header["Bills"]);
            Assert.Equal("https://civic-lens.test/web/executive-actions", header["Executive actions"]);
        }
    }

    [Fact]
    public async Task ANoReleaseAnswers503AndACursorOfAnotherReleaseRedirectsToTheFirstPage()
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite(), options => options with { PublicBaseUrl = BaseUrl });
        foreach (var path in new[] { "/web/votes", "/web/bills", "/web/statements", "/web/executive-actions" })
            await GetPageAsync(host, path, HttpStatusCode.ServiceUnavailable);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);

        var stale = new VoteCursor(99, new DateOnly(2026, 3, 1), PublishedChamber.House, 1, 1, new string('a', 64)).Encode();
        using var noRedirect = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = host.Client.BaseAddress };
        var response = await noRedirect.GetAsync($"/web/votes?chamber=house&cursor={stale}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/web/votes?chamber=house", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ARefusedFilterAnswers400WithThePageTheTypedTextAndTheRefusedFieldMarkedAndNoRows()
    {
        await using var host = await StartAsync();
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote('a', "2026-03-02", 1));
        await BillsSeed.AddBillsAsync(host.OperationalDatabase, new SeededBill('a', "2026-03-02"));

        var bill = await GetPageAsync(host, "/web/bills?bill=5334a&sponsor=Ann", HttpStatusCode.BadRequest);
        var conflict = await GetPageAsync(host, "/web/bills?type=s&bill=H.R.%201002", HttpStatusCode.BadRequest);
        var date = await GetPageAsync(host, "/web/votes?from=2026-13-45&chamber=house", HttpStatusCode.BadRequest);
        var order = await GetPageAsync(host, "/web/statements?from=2026-02-01&to=2026-01-01", HttpStatusCode.BadRequest);
        var choice = await GetPageAsync(host, "/web/executive-actions?type=law", HttpStatusCode.BadRequest);

        var number = bill.QuerySelector("input[name=bill]")!;
        Assert.Equal(("5334a", "true", null), (number.GetAttribute("value"), number.GetAttribute("aria-invalid"), number.GetAttribute("aria-describedby")));
        Assert.Equal("Ann", bill.QuerySelector("input[name=sponsor]")!.GetAttribute("value"));
        Assert.Null(bill.QuerySelector("input[name=sponsor]")!.GetAttribute("aria-invalid"));
        Assert.Equal("true", conflict.QuerySelector("select[name=type]")!.GetAttribute("aria-invalid"));
        var from = date.QuerySelector("input[name=from]")!;
        Assert.Equal("true", from.GetAttribute("aria-invalid"));
        Assert.Equal(RecordListFilter.DateError, date.GetElementById(from.GetAttribute("aria-describedby")!)!.TextContent);
        Assert.Equal("house", date.QuerySelector("select[name=chamber] option[selected]")!.GetAttribute("value"));
        Assert.Equal("true", order.QuerySelector("input[name=from]")!.GetAttribute("aria-invalid"));
        Assert.Null(order.QuerySelector("input[name=from]")!.GetAttribute("aria-describedby"));
        Assert.Equal("true", choice.QuerySelector("select[name=type]")!.GetAttribute("aria-invalid"));
        foreach (var page in new[] { bill, conflict, date, order, choice })
        {
            Assert.NotNull(page.QuerySelector(".list-fold[open] form"));
            Assert.Empty(Rows(page));
            Assert.Null(page.QuerySelector(".list-status"));
            Assert.Null(page.QuerySelector(".empty"));
            Assert.Equal(1, page.QuerySelectorAll("[aria-invalid]").Length);
        }
        Assert.Single(date.QuerySelectorAll(".field-error"));
        Assert.Empty(bill.QuerySelectorAll(".field-error"));
    }

    [Fact]
    public async Task AReleaseWithNoRecordsShowsNoTotalNoFiltersAndNoNoMatchTips()
    {
        await using var host = await StartAsync();

        foreach (var path in new[] { "/web/votes", "/web/bills", "/web/statements", "/web/executive-actions" })
        {
            var page = await GetPageAsync(host, path);
            Assert.DoesNotContain("in this release", Lede(page));
            Assert.Null(page.QuerySelector(".list-fold"));
            Assert.Null(page.QuerySelector(".list-status"));
            Assert.Null(page.QuerySelector(".empty"));
            Assert.Empty(Rows(page));
        }
    }

    [Fact]
    public async Task TheFilterFoldOpensOnAWideScreenByScriptAndTheNoMatchHeadingFollowsThePageTitle()
    {
        await using var host = await StartAsync();
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote('a', "2026-03-02", 1));

        var page = await GetPageAsync(host, "/web/votes");
        var empty = await GetPageAsync(host, "/web/votes?chamber=senate");
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

        var response = await host.Client.GetAsync("/web/votes");
        var page = await GetPageAsync(host, "/web/votes");

        Assert.Equal(PageCache.CacheControl, string.Join(", ", response.Headers.GetValues("Cache-Control")));
        Assert.Equal(["Headline b", "Headline a"], Titles(page));
        var (person, model) = (Rows(page)[0], Rows(page)[1]);
        Assert.Null(Label(person));
        Assert.Equal(ModelLabel, Label(model));
        Assert.Equal(["Headline a", ModelLabel], model.QuerySelectorAll("a > span").Take(2).Select(span => span.TextContent));
        Assert.Contains("Voted on 2 Mar 2026, House roll call 1, Vote to pass or agree to, H.R. 1", Text(model));
        Assert.Contains("2 Yes", Text(model));
        Assert.Equal("Showing 2 votes on this page.", page.QuerySelector(".list-status")!.TextContent);
        Assert.Empty(Chips(page));
        Assert.Equal("2 votes are in this release. House and Senate roll call votes, newest first. To see how one member voted, open their page from Officials.", Lede(page));
        var officials = page.QuerySelector(".page-header-lede a")!;
        Assert.Equal(("Officials", "https://civic-lens.test/officials.html"), (officials.TextContent, officials.GetAttribute("href")));
        Assert.Null(page.QuerySelector(".page-header-meta"));
        Assert.Equal("model", model.QuerySelector(".record-row-label")!.GetAttribute("data-origin"));
    }

    [Fact]
    public async Task TheVoteListFiltersByChamberKindBillNumberAndDateAndKeepsTheFiltersInTheForm()
    {
        await using var host = await StartAsync();
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase,
            Vote('a', "2026-03-01", 1),
            Vote('b', "2026-03-02", 2) with { Chamber = "Senate", Kind = "Cloture", BillType = "s", BillNumber = 5 },
            Vote('c', "2026-03-03", 3) with { Kind = "Amendment" });

        Assert.Equal(["Headline b"], Titles(await GetPageAsync(host, "/web/votes?chamber=senate")));
        Assert.Equal(["Headline b"], Titles(await GetPageAsync(host, "/web/votes?kind=cloture")));
        Assert.Equal(["Headline b"], Titles(await GetPageAsync(host, "/web/votes?bill=S.%205")));
        Assert.Equal(["Headline c"], Titles(await GetPageAsync(host, "/web/votes?kind=amendment&from=2026-03-03")));
        Assert.Equal(["Headline c", "Headline a"], Titles(await GetPageAsync(host, "/web/votes?bill=1")));
        var page = await GetPageAsync(host, "/web/votes?chamber=senate&from=2026-03-01");
        Assert.Equal("Showing 1 vote on this page.", page.QuerySelector(".list-status")!.TextContent);
        Assert.Equal(["Senate", "On or after 1 Mar 2026"], Chips(page));
        Assert.StartsWith("3 votes are in this release.", Lede(page));
        Assert.Equal(["Bill S. 5", "From 1 Mar 2026 to 2 Mar 2026"], Chips(await GetPageAsync(host, "/web/votes?bill=S.%205&from=2026-03-01&to=2026-03-02")));
        Assert.Equal(["Vote to end debate", "On or before 9 Mar 2026"], Chips(await GetPageAsync(host, "/web/votes?kind=cloture&to=2026-03-09")));
        Assert.Equal("senate", page.QuerySelector("select[name=chamber] option[selected]")!.GetAttribute("value"));
        Assert.Equal("2026-03-01", page.QuerySelector("input[name=from]")!.GetAttribute("value"));
        Assert.NotNull(page.QuerySelector(".list-fold[open]"));
        Assert.NotNull(page.QuerySelector("a[href='?']"));
    }

    [Fact]
    public async Task TheVoteListPagesByTwentyFiveKeepsItsFiltersAndTheEmptyStateOffersNextSteps()
    {
        await using var host = await StartAsync();
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, [.. Enumerable.Range(1, 26).Select(index =>
            Vote((char)('A' + index), $"2026-03-{index:00}", index) with { RecordId = new string((char)('a' + index % 6), 60) + index.ToString("0000") })]);

        var first = await GetPageAsync(host, "/web/votes?chamber=house");
        var next = first.QuerySelector(".pager a[rel=next]")!.GetAttribute("href")!;
        var second = await GetPageAsync(host, "/web/votes" + next);

        Assert.Equal(VoteListReader.PageSize, Rows(first).Length);
        Assert.StartsWith("?chamber=house&cursor=", next);
        Assert.Single(Rows(second));
        Assert.Null(second.QuerySelector(".pager"));
        Assert.Empty(Titles(first).Intersect(Titles(second)));
        var empty = await GetPageAsync(host, "/web/votes?chamber=senate");
        Assert.Contains("No vote matches these filters.", empty.QuerySelector(".empty")!.TextContent);
        Assert.Empty(Rows(empty));
        Assert.Equal("Showing 0 votes on this page.", empty.QuerySelector(".list-status")!.TextContent);
        Assert.Equal(["Senate"], Chips(empty));
    }

    // ---- bills ----

    private static string BillId(char id) => new(id, 64);

    private static string StoredStatus(char id, string text, string? status) => PublicRecordPageRows.PinnedStatus(1, BillId(id), text, "2026-02-13", PublishedAt.UtcTicks)
        + (status is null ? "" : $"\nUPDATE release_bill_statuses SET status = '{status}' WHERE record_id = '{BillId(id)}';");

    [Fact]
    public async Task TheBillListShowsSponsorLatestActionTheStoredStatusBadgeAndTheAiLabelOnlyWhenTheViewHasOne()
    {
        await using var host = await StartAsync();
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase, new SeededOfficial("p1", "Ann Able"));
        await BillsSeed.AddBillsAsync(host.OperationalDatabase,
            new SeededBill('a', "2026-03-02") { Sponsorships = [new("p1", "Sponsor", "2026-03-02")] },
            new SeededBill('b', "2026-03-03") { TextOrigin = "Person", BillNumber = 8, Sponsorships = [new("p2", "Sponsor", "2026-03-03")] },
            new SeededBill('c', "2026-03-04") { BillNumber = 9 });
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;\n"
            + StoredStatus('a', "Referred to the House Committee on Energy and Commerce.", "InCommittee") + "\n"
            + StoredStatus('c', "Placed on the Union Calendar, Calendar No. 12.", "Other"));

        var page = await GetPageAsync(host, "/web/bills");

        var (other, person, model) = (Rows(page)[0], Rows(page)[1], Rows(page)[2]);
        Assert.Null(Label(person));
        Assert.Equal(ModelLabel, Label(model));
        Assert.Equal("model", model.QuerySelector(".record-row-label")!.GetAttribute("data-origin"));
        Assert.DoesNotContain("Sponsor:", Text(person));
        Assert.Contains("Sponsor: Ann Able", Text(model));
        Assert.Contains("Latest sponsorship 2 Mar 2026", Text(model));
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
    public async Task TheBillListNamesTheNewestCongressStatesNoReleaseTotalAndOffersOnlyThePolicyAreasOfThatCongress()
    {
        await using var host = await StartAsync();
        await BillsSeed.AddBillsAsync(host.OperationalDatabase, new SeededBill('a', "2026-03-02") { PolicyAreaId = "tax" },
            new SeededBill('b', "2024-03-02") { Congress = 118, BillNumber = 8, PolicyAreaId = "energy" });
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase,
            "INSERT INTO release_bill_counts (release_number, congress, bills, expected_member_row_count) VALUES (1, 119, 41, 0), (1, 118, 3, 0);");

        var page = await GetPageAsync(host, "/web/bills");

        Assert.Equal(["a"], Ids(page));
        Assert.Equal("Bills and resolutions in the 119th Congress, with the latest official action on each. A bill moves up the list when a member signs on to it.", Lede(page));
        Assert.Equal(["tax"], page.QuerySelectorAll("select[name=policyArea] option").Skip(1).Select(option => option.GetAttribute("value")));
    }

    [Fact]
    public async Task TheBillListFiltersByTypeStatusSponsorNameIssuePolicyAreaBillNumberAndDateAndPagesByTwentyFive()
    {
        await using var host = await StartAsync();
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase, new SeededOfficial("p1", "Ann Able"), new SeededOfficial("p2", "Bo Zed"));
        await BillsSeed.AddBillsAsync(host.OperationalDatabase,
            new SeededBill('a', "2026-03-01") { Issues = ["health"], BillNumber = 4, Sponsorships = [new("p1", "Sponsor", "2026-03-01"), new("p2", "Cosponsor", "2026-03-01")] },
            new SeededBill('b', "2026-03-02") { BillType = "s", BillNumber = 4, PolicyAreaId = "tax", Sponsorships = [new("p2", "Sponsor", "2026-03-02"), new("p1", "Cosponsor", "2026-03-02")] },
            new SeededBill('c', "2026-03-03") { BillNumber = 9, Sponsorships = [new("p3", "Sponsor", "2026-03-03")] });
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;\n"
            + StoredStatus('a', "Received in the Senate.", "PassedHouse") + "\n" + StoredStatus('c', "Became Public Law No: 119-1.", null) + "\n"
            + "INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (1, 'health', 'Health care', 'Defined');");

        Assert.Equal(["a"], Ids(await GetPageAsync(host, "/web/bills?issue=health")));
        Assert.Equal(["b"], Ids(await GetPageAsync(host, "/web/bills?policyArea=tax")));
        Assert.Equal(["b"], Ids(await GetPageAsync(host, "/web/bills?bill=S.%204")));
        Assert.Equal(["b"], Ids(await GetPageAsync(host, "/web/bills?type=s")));
        Assert.Equal(["a", "b"], Ids(await GetPageAsync(host, "/web/bills?bill=4")).Order());
        Assert.Equal(["a"], Ids(await GetPageAsync(host, "/web/bills?bill=4&type=hr")));
        Assert.Equal(["c"], Ids(await GetPageAsync(host, "/web/bills?from=2026-03-03")));
        Assert.Equal(["a"], Ids(await GetPageAsync(host, "/web/bills?status=passedHouse")));
        Assert.Empty(Ids(await GetPageAsync(host, "/web/bills?status=becameLaw")));
        Assert.Equal(["a"], Ids(await GetPageAsync(host, "/web/bills?sponsor=ab")));
        Assert.Equal(["b"], Ids(await GetPageAsync(host, "/web/bills?sponsor=ZED")));
        await GetPageAsync(host, "/web/bills?type=law", HttpStatusCode.BadRequest);
        await GetPageAsync(host, "/web/bills?status=passed", HttpStatusCode.BadRequest);
        var filtered = await GetPageAsync(host, "/web/bills?type=s&sponsor=Bo&policyArea=tax&bill=4&status=passedHouse&issue=health&to=2026-03-09");
        var taxArea = filtered.QuerySelector("select[name=policyArea] option[value=tax]")!.TextContent;
        Assert.Equal(["S. (Senate bill)", "Number S. 4", "Sponsor: Bo", "Passed the House", "Health care", taxArea, "On or before 9 Mar 2026"], Chips(filtered));
        Assert.Equal("tax", filtered.QuerySelector("select[name=policyArea] option[selected]")!.GetAttribute("value"));
        Assert.Equal("Bo", filtered.QuerySelector("input[name=sponsor]")!.GetAttribute("value"));
        Assert.Contains(filtered.QuerySelectorAll("select[name=policyArea] option").Select(option => option.GetAttribute("value")), value => value == "health");
        Assert.Contains("No bill matches these filters.", (await GetPageAsync(host, "/web/bills?issue=nothing")).QuerySelector(".empty")!.TextContent);

        await BillsSeed.AddBillsAsync(host.OperationalDatabase, [.. Enumerable.Range(0, 25).Select(index =>
            new SeededBill((char)('d' + index % 20), $"2026-02-{index + 1:00}") { RecordId = new string('d', 60) + index.ToString("0000"), BillNumber = 100 + index })]);
        var first = await GetPageAsync(host, "/web/bills");
        var next = first.QuerySelector(".pager a[rel=next]")!.GetAttribute("href")!;
        var second = await GetPageAsync(host, "/web/bills" + next);
        Assert.Equal(BillListReader.PageSize, Rows(first).Length);
        Assert.Equal(3, Rows(second).Length);
        Assert.Empty(Titles(first).Intersect(Titles(second)));
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
    public async Task TheStatementListShowsEachSpeakerWithSeatAndPartyTheDateTheSourceSiteTheVoteLinkAndTheAiLabelOnlyWhenTheViewHasOne()
    {
        await using var host = await StartAsync();
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase,
            new SeededOfficial("official-a", "Ann Able", new SeededTerm("House", "Representative", "2025-01-03") { State = "MA", District = 5, Party = "Democrat" }),
            new SeededOfficial("official-z", "Zed Zane", new SeededTerm("Senate", "Senator", "2025-01-03") { State = "AZ", Party = "Republican" }));
        await AddStatementsAsync(host,
            new('a', "2026-03-02", ["official-z", "official-a"], [], "Analysis", "Code"),
            new('b', null, ["official-a"], [], "Person", "Person"));
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote('f', "2026-03-01", 1));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;\n"
            + PublicRecordPageRows.Link(new string('7', 64), new string('a', 64), new string('f', 64), "vote", "StatementAboutVote", "2026-03-02"));

        var page = await GetPageAsync(host, "/web/statements");

        Assert.Equal(["Headline a", "Headline b"], Titles(page));
        var (model, person) = (Rows(page)[0], Rows(page)[1]);
        Assert.StartsWith("An AI summary", model.QuerySelector(".record-row-label")!.TextContent);
        Assert.Equal("model", model.QuerySelector(".record-row-label")!.GetAttribute("data-origin"));
        Assert.Null(person.QuerySelector(".record-row-label"));
        Assert.Contains("Zed Zane, Senator for AZ, Republican, Ann Able, Representative for MA, district 5, Democrat, Said on 2 Mar 2026, From site-a.senate.gov", Text(model));
        Assert.Contains("Ann Able, Representative for MA, district 5, Democrat, Date not stated, From site-b.senate.gov", Text(person));
        Assert.Equal("Linked to a vote", model.QuerySelector(".badge")!.TextContent.Trim());
        Assert.Null(person.QuerySelector(".badge"));
        Assert.Equal("Showing 2 statements on this page.", page.QuerySelector(".list-status")!.TextContent);
        Assert.StartsWith("2 statements are in this release. What members of Congress", Lede(page));
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

        Assert.Equal(["Headline b", "Headline c"], Titles(await GetPageAsync(host, "/web/statements?official=Ann")));
        Assert.Equal(["Headline b", "Headline c"], Titles(await GetPageAsync(host, "/web/statements?official=able")));
        Assert.Empty(Titles(await GetPageAsync(host, "/web/statements?official=nn")));
        Assert.Equal(["Headline a"], Titles(await GetPageAsync(host, "/web/statements?issue=health")));
        Assert.Equal(["Headline b"], Titles(await GetPageAsync(host, "/web/statements?from=2026-03-03")));
        var page = await GetPageAsync(host, "/web/statements?official=Zed&issue=health&from=2026-03-01&to=2026-03-02");
        Assert.Equal(["Headline a"], Titles(page));
        Assert.Equal(["Official: Zed", "Health care", "From 1 Mar 2026 to 2 Mar 2026"], Chips(page));
        Assert.Equal("Zed", page.QuerySelector("input[name=official]")!.GetAttribute("value"));
        Assert.Equal("Health care", page.QuerySelector("select[name=issue] option[value=health]")!.TextContent);
        Assert.Contains("No statement matches these filters.", (await GetPageAsync(host, "/web/statements?official=nobody")).QuerySelector(".empty")!.TextContent);
    }

    [Fact]
    public async Task TheStatementListPagesByTwentyFiveAcrossDatedAndUndatedStatements()
    {
        await using var host = await StartAsync();
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase, new SeededOfficial("official-a", "Ann Able"));
        await AddStatementsAsync(host, [.. Enumerable.Range(1, 26).Select(index =>
            new SeededStatement('a', index <= 24 ? $"2026-03-{index:00}" : null, ["official-a"], [], "Person", "Person") { RecordId = new string('a', 60) + index.ToString("0000") })]);

        var first = await GetPageAsync(host, "/web/statements");
        var second = await GetPageAsync(host, "/web/statements" + first.QuerySelector(".pager a[rel=next]")!.GetAttribute("href"));

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

        var page = await GetPageAsync(host, "/web/executive-actions");

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

        var notices = await GetPageAsync(host, "/web/executive-actions?type=notice");
        Assert.Equal(["Notice zero"], Titles(notices));
        Assert.Equal(["Presidential notices"], Chips(notices));
        Assert.StartsWith("26 executive actions are in this release.", Lede(notices));
        var typeChips = new List<string>();
        foreach (var type in new[] { "executiveOrder", "proclamation", "notice", "memorandum" })
            typeChips.AddRange(Chips(await GetPageAsync(host, $"/web/executive-actions?type={type}")));
        Assert.Equal(["Executive orders", "Proclamations", "Presidential notices", "Memorandums"], typeChips);
        Assert.Equal(["Order 25"], Titles(await GetPageAsync(host, "/web/executive-actions?type=executiveOrder&from=2026-01-25")));
        var first = await GetPageAsync(host, "/web/executive-actions");
        var next = first.QuerySelector(".pager a[rel=next]")!.GetAttribute("href")!;
        var second = await GetPageAsync(host, "/web/executive-actions" + next);
        Assert.Equal(25, Rows(first).Length);
        Assert.Equal(["Notice zero"], Titles(second));
        Assert.Contains("Memorandum", (await GetPageAsync(host, "/web/executive-actions")).QuerySelectorAll("select[name=type] option").Select(option => option.TextContent));
        Assert.Contains("No executive action matches these filters.", (await GetPageAsync(host, "/web/executive-actions?type=memorandum")).QuerySelector(".empty")!.TextContent);
    }
}
