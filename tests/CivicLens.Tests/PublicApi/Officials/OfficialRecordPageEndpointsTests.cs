using System.Globalization;
using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.Application.Roster;
using CivicLens.PublicApi.Officials;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Home;
using CivicLens.Tests.PublicApi.Statements;
using CivicLens.Tests.PublicApi.Votes;
using Npgsql;

namespace CivicLens.Tests.PublicApi.Officials;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class OfficialRecordPageEndpointsTests(PostgresCollection postgres)
{
    private static readonly PublicBaseUrl BaseUrl = PublicBaseUrl.Parse("https://civic-lens.test", "test")!;

    private static readonly SeededOfficial[] Officials =
    [
        new("p0", "Ann Able", new SeededTerm("Representative", "Representative", "2021-01-03") { State = "MA", District = 7, Party = "Democrat", EndsOn = "2023-01-03" },
            new SeededTerm("Senator", "Senator", "2023-01-03") { State = "MA", Party = "Democrat" }),
        new("p1", "Ben Baker", new SeededTerm("Representative", "Representative", "2025-01-03") { State = "TX", District = 3, Party = "Republican" }),
        new("president", "Eve East", new SeededTerm(ExecutiveRosterParser.President, "Executive", "2025-01-20") { Party = "Republican" }),
        new("bare", "Cy Cole")
    ];

    private async Task<ApiHost> StartAsync()
    {
        var host = await ApiHost.StartAsync(postgres, options => options with { PublicBaseUrl = BaseUrl });
        var database = host.OperationalDatabase;
        await ViewsSeed.ActivateReleaseAsync(database, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await OfficialsSeed.AddOfficialsAsync(database, Officials);
        return host;
    }

    private static string Id(int number) => number.ToString("x64", CultureInfo.InvariantCulture);

    // Twenty-seven votes of p0 on one day, so the list needs a second page and the page break splits a day. Ties sort by record ID.
    private static async Task AddManyVotesAsync(ApiHost host) =>
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, [.. Enumerable.Range(0, 27).Select(number => new SeededVote("0123456789abcdef"[number % 16],
            "2026-01-01")
        { Casts = ["Yea", "Nay"], PersonIds = ["p0", "p1"], RollCall = number + 1, RecordId = Id(number) })]);

    private static async Task<IDocument> GetPageAsync(ApiHost host, string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await host.Client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{path} gave {(int)response.StatusCode}: {body}");
        return await new HtmlParser().ParseDocumentAsync(body);
    }

    private static string[] Tabs(IDocument page) => [.. page.QuerySelectorAll(".segmented-item").Select(tab => tab.TextContent.Trim())];

    [Fact]
    public async Task TheVotesTabListsTheVotesOfTheOfficialNewestFirstAsSharedSingleMemberRowsWithTheirCast()
    {
        await using var host = await StartAsync();
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase,
            new SeededVote('a', "2026-02-01") { Casts = ["Nay", "Yea"], PersonIds = ["p0", "p1"], RollCall = 1 },
            new SeededVote('b', "2026-02-03") { Casts = ["Yea"], PersonIds = ["p1"], RollCall = 2 },
            new SeededVote('c', "2026-02-05") { Casts = ["Yea", "Nay"], PersonIds = ["p0", "p1"], RollCall = 3 });

        var page = await GetPageAsync(host, "/officials/p0/record.html");

        Assert.Equal("All records of Ann Able", page.QuerySelector("h1")!.TextContent);
        Assert.Equal(["Votes2"], Tabs(page));
        Assert.Contains("This list holds 2 votes of Ann Able in this release", page.QuerySelector(".records-lead p")!.TextContent);
        var rows = page.QuerySelectorAll(".shared-vote-row").ToArray();
        Assert.Equal([BaseUrl.Join($"records/{new string('c', 64)}.html"), BaseUrl.Join($"records/{new string('a', 64)}.html")],
            rows.Select(row => row.QuerySelector("a")!.GetAttribute("href")!).ToArray());
        Assert.Equal(["Yes (Yea)", "No (Nay)"], rows.Select(row => row.QuerySelector(".shared-vote-cast")!.TextContent.Trim().Replace("Ann Able", "").Trim()).ToArray());
        Assert.Equal(BaseUrl.Join("officials/p0.html"), page.QuerySelector(".page-header-meta a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task TheVotesTabColorsALabelAsAiOnlyWhenTheViewSaysAModelWroteTheHeadline()
    {
        await using var host = await StartAsync();
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase,
            new SeededVote('a', "2026-02-01") { Casts = ["Nay"], PersonIds = ["p0"], RollCall = 1, TextOrigin = "Code" },
            new SeededVote('c', "2026-02-05") { Casts = ["Yea"], PersonIds = ["p0"], RollCall = 3, TextOrigin = "Model" });

        var labels = (await GetPageAsync(host, "/officials/p0/record.html")).QuerySelectorAll(".shared-vote-meta .ai-label");

        Assert.Equal(["model", "code"], labels.Select(label => label.GetAttribute("data-origin")));
        Assert.Equal([AiLabelWords.ModelWording, AiLabelWords.CodeWording], labels.Select(label => label.TextContent.Trim()));
    }

    [Fact]
    public async Task TheListPagesForwardByTwentyFiveWithOneCursorAndTheLastPageHasNoNextLink()
    {
        await using var host = await StartAsync();
        await AddManyVotesAsync(host);

        var first = await GetPageAsync(host, "/officials/p0/record.html");
        var next = first.QuerySelector(".pager a[rel=next]")!.GetAttribute("href")!;
        var second = await GetPageAsync(host, next.Replace("https://civic-lens.test/", "/"));

        Assert.Equal(25, first.QuerySelectorAll(".shared-vote-row").Length);
        Assert.Contains("kind=votes&cursor=r.1.", next);
        Assert.Equal(2, second.QuerySelectorAll(".shared-vote-row").Length);
        Assert.Null(second.QuerySelector(".pager a[rel=next]"));
        Assert.Equal("2 on this page. 27 in all.", second.QuerySelector(".pager span")!.TextContent);
        var seen = first.QuerySelectorAll(".shared-vote-row h2 a").Concat(second.QuerySelectorAll(".shared-vote-row h2 a")).Select(link => link.GetAttribute("href")!).ToArray();
        Assert.Equal(27, seen.Distinct().Count());
        Assert.Equal(Enumerable.Range(0, 27).Select(number => BaseUrl.Join($"records/{Id(number)}.html")).ToArray(), seen);
    }

    // Four votes of p0, a Democrat: a split vote against the party on a tagged bill, a split vote with the party, a No on another bill, and a Senate vote.
    private static async Task AddFilteredVotesAsync(ApiHost host)
    {
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase,
            new SeededVote('a', "2026-02-01") { Casts = ["Nay", "Yea"], PersonIds = ["p0", "p1"], RollCall = 1, BillNumber = 5334, Issues = ["health"] },
            new SeededVote('b', "2026-02-02") { Casts = ["Yea", "Nay"], PersonIds = ["p0", "p1"], RollCall = 2, BillNumber = 5334 },
            new SeededVote('c', "2026-02-03") { Casts = ["Nay"], PersonIds = ["p0"], RollCall = 3, BillType = "s", BillNumber = 12 },
            new SeededVote('d', "2026-02-04") { Casts = ["Aye"], PersonIds = ["p0"], RollCall = 4, BillNumber = 7, Issues = ["health"] });
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            SET session_replication_role = replica;
            UPDATE vote_member_list_entries SET party = CASE person_id WHEN 'p0' THEN 'Democrat' WHEN 'p1' THEN 'Republican' END;
            UPDATE vote_revisions SET columns_written = true, alignment = 'Split', democrat_majority = 'Yea', republican_majority = 'Nay'
              WHERE record_id IN ('{new string('a', 64)}', '{new string('b', 64)}');
            INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (1, 'health', 'Health care', 'Defined');
            """);
    }

    private static string[] Rows(IDocument page) => [.. page.QuerySelectorAll(".shared-vote-row h2 a").Select(link => link.GetAttribute("href")![^69..^64])];

    private static string[] Chips(IDocument page) => [.. page.QuerySelectorAll(".list-chips li").Select(chip => chip.TextContent.Trim())];

    [Theory]
    [InlineData("issue=health", "da", "Health care")]
    [InlineData("bill=H.R.%205334", "ba", "Bill H.R. 5334")]
    [InlineData("bill=5334", "ba", "Bill 5334")]
    [InlineData("party=against", "a", "Against their party")]
    [InlineData("cast=yes", "db", "Voted Yes")]
    [InlineData("cast=no", "ca", "Voted No")]
    public async Task EachVoteFilterNarrowsTheVotesTabAndNamesItselfInAChip(string query, string expected, string chip)
    {
        await using var host = await StartAsync();
        await AddFilteredVotesAsync(host);

        var page = await GetPageAsync(host, "/officials/p0/record.html?kind=votes&" + query);

        Assert.Equal([.. expected.Select(id => new string(id, 5))], Rows(page));
        Assert.Equal([chip], Chips(page));
        Assert.Equal($"Showing {expected.Length} {(expected.Length == 1 ? "vote" : "votes")} on this page.", page.QuerySelector(".list-status")!.TextContent);
        Assert.Equal(["Votes4"], Tabs(page));
    }

    [Fact]
    public async Task TheFiltersCombineAndAFilterWithoutAKindSelectsTheVotesTab()
    {
        await using var host = await StartAsync();
        await AddFilteredVotesAsync(host);
        await BillsSeed.AddBillsAsync(host.OperationalDatabase, new SeededBill('9', "2026-03-02") { Sponsorships = [new("p0", "Sponsor", "2026-01-10")] });

        var page = await GetPageAsync(host, "/officials/p0/record.html?issue=health&cast=yes");

        Assert.Equal(["ddddd"], Rows(page));
        Assert.Equal(["Health care", "Voted Yes"], Chips(page));
        Assert.StartsWith("Votes", page.QuerySelector(".segmented-item[aria-current=true]")!.TextContent.Trim());
    }

    [Fact]
    public async Task TheFormHoldsTheFourApprovedFieldsKeepsTheTabAndClosesOnceAFilterApplies()
    {
        await using var host = await StartAsync();
        await AddFilteredVotesAsync(host);

        var plain = await GetPageAsync(host, "/officials/p0/record.html");
        var filtered = await GetPageAsync(host, "/officials/p0/record.html?kind=votes&party=against");

        var form = plain.QuerySelector(".list-fold form[method=get]")!;
        Assert.Equal(["kind", "issue", "bill", "party", "cast"], form.QuerySelectorAll("input[name], select[name]").Select(field => field.GetAttribute("name")));
        Assert.Equal("votes", form.QuerySelector("input[type=hidden][name=kind]")!.GetAttribute("value"));
        Assert.Equal(["Issue", "Bill number", "Party line", "Vote"], form.QuerySelectorAll(".field-label").Select(label => label.TextContent));
        Assert.Equal(["", "health"], form.QuerySelectorAll("select[name=issue] option").Select(option => option.GetAttribute("value")));
        Assert.Null(plain.QuerySelector(".list-status"));
        Assert.NotNull(plain.QuerySelector("details.list-fold[data-wide-open]:not([open])"));
        Assert.NotNull(filtered.QuerySelector("details.list-fold:not([open]):not([data-wide-open])"));
        Assert.True(filtered.QuerySelector("select[name=party] option[value=against]")!.HasAttribute("selected"));
        Assert.Equal(BaseUrl.Join("officials/p0/record.html?kind=votes"), filtered.QuerySelector(".lists-filter-actions a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task TheNextPageKeepsTheFiltersAndAFilteredPagerNamesNoTotal()
    {
        await using var host = await StartAsync();
        await AddManyVotesAsync(host);

        var first = await GetPageAsync(host, "/officials/p0/record.html?kind=votes&cast=yes");
        var next = first.QuerySelector(".pager a[rel=next]")!.GetAttribute("href")!;
        var second = await GetPageAsync(host, next.Replace("https://civic-lens.test/", "/"));

        Assert.Contains("kind=votes&cast=yes&cursor=r.1.", next);
        Assert.Equal((25, 2), (first.QuerySelectorAll(".shared-vote-row").Length, second.QuerySelectorAll(".shared-vote-row").Length));
        Assert.Equal(["Voted Yes"], Chips(second));
        Assert.Null(second.QuerySelector(".pager"));
        Assert.DoesNotContain("in all", first.QuerySelector(".pager")!.TextContent);
    }

    [Fact]
    public async Task ANoMatchShowsTheEmptyLineAndARefusedFilterAnswers400WithTheMarkedFieldAndNoRows()
    {
        await using var host = await StartAsync();
        await AddFilteredVotesAsync(host);

        var none = await GetPageAsync(host, "/officials/p0/record.html?kind=votes&bill=S.%2012&cast=yes");
        var refused = await GetPageAsync(host, "/officials/p0/record.html?kind=votes&bill=not-a-bill&cast=yes", HttpStatusCode.BadRequest);
        var wrongTab = await GetPageAsync(host, "/officials/p0/record.html?kind=bills&cast=yes", HttpStatusCode.BadRequest);
        await GetPageAsync(host, "/officials/p0/record.html?cast=maybe", HttpStatusCode.BadRequest);

        Assert.Equal("No vote matches these filters.", none.QuerySelector(".empty h2")!.TextContent.Trim());
        Assert.Empty(none.QuerySelectorAll(".shared-vote-row"));
        Assert.Empty(refused.QuerySelectorAll(".shared-vote-row"));
        Assert.Null(refused.QuerySelector(".list-status"));
        Assert.NotNull(refused.QuerySelector("details.list-fold[open]"));
        Assert.Equal("true", refused.QuerySelector("input[name=bill]")!.GetAttribute("aria-invalid"));
        Assert.Equal("not-a-bill", refused.QuerySelector("input[name=bill]")!.GetAttribute("value"));
        Assert.Equal(RecordListFilter.BillNumberError, refused.QuerySelector(".field-error")!.TextContent);
        Assert.Empty(wrongTab.QuerySelectorAll(".shared-vote-row"));
    }

    [Fact]
    public async Task AListOfOnePageShowsNoPagerLine()
    {
        await using var host = await StartAsync();
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, new SeededVote('a', "2026-02-01") { Casts = ["Nay"], PersonIds = ["p0"], RollCall = 1 });

        var page = await GetPageAsync(host, "/officials/p0/record.html");

        Assert.Null(page.QuerySelector(".pager"));
    }

    [Fact]
    public async Task VoteRowsAreH2UnderTheH1AndNameTheChamberOfEachRollCallForAMemberWhoHeldTwoSeats()
    {
        await using var host = await StartAsync();
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase,
            new SeededVote('a', "2022-02-01") { Casts = ["Nay"], PersonIds = ["p0"], RollCall = 20 },
            new SeededVote('b', "2026-02-03") { Chamber = "Senate", Casts = ["Yea"], PersonIds = ["p0"], RollCall = 20 });

        var page = await GetPageAsync(host, "/officials/p0/record.html");

        Assert.Equal(["h1", "h2", "h2"], page.QuerySelectorAll("main h1, main h2, main h3").Select(heading => heading.LocalName).Take(3).ToArray());
        Assert.Empty(page.QuerySelectorAll(".shared-vote-row h3"));
        var meta = page.QuerySelectorAll(".shared-vote-meta").Select(line => line.TextContent).ToArray();
        Assert.Contains("Senate roll call 20,", meta[0]);
        Assert.Contains("House roll call 20,", meta[1]);
    }

    [Fact]
    public async Task ACursorOfAnotherReleaseReadsNoRowsAndNoOfficesBeforeTheRedirect()
    {
        await using var host = await StartAsync();
        await AddManyVotesAsync(host);
        await using var views = NpgsqlDataSource.Create(host.Options.PublicViewsDatabase);
        var reader = new OfficialRecordPageReader(views);

        var stale = await reader.ReadAsync("p0", new(OfficialRecordKind.Votes, new(7, new(2026, 1, 10), Id(3))), CancellationToken.None);
        var current = await reader.ReadAsync("p0", new(OfficialRecordKind.Votes, new(1, new(2026, 1, 10), Id(3))), CancellationToken.None);

        Assert.Equal(1, stale!.Release);
        Assert.Empty(stale.Votes);
        Assert.Empty(stale.Offices);
        Assert.NotEmpty(current!.Votes);
        Assert.NotEmpty(current.Offices);
        Assert.True(current.LaterPage);
    }

    [Fact]
    public async Task ACursorOfAnotherReleaseRedirectsToTheFirstPageOfTheSameKind()
    {
        await using var host = await StartAsync();
        await AddManyVotesAsync(host);

        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = host.Client.BaseAddress };
        using var response = await client.GetAsync($"/officials/p0/record.html?kind=votes&cursor=r.7.20260110.{Id(3)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(BaseUrl.Join("officials/p0/record.html?kind=votes"), response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task TheBillsTabListsOnlyTheBillsTheOfficialSponsoredWithTheStatusBadgeAndTheBillLineNote()
    {
        await using var host = await StartAsync();
        await BillsSeed.AddBillsAsync(host.OperationalDatabase,
            new SeededBill('1', "2026-03-02") { BillNumber = 1, Sponsorships = [new("p0", "Sponsor", "2026-01-10")] },
            new SeededBill('2', "2026-03-03") { BillNumber = 2, BillLine = "Pays landlords.", Sponsorships = [new("p0", "Sponsor", "2026-03-03"), new("p1", "Cosponsor", "2026-03-03")] },
            new SeededBill('3', "2026-03-04") { BillNumber = 3, Sponsorships = [new("p1", "Sponsor", "2026-03-04")] });

        var page = await GetPageAsync(host, "/officials/p0/record.html?kind=bills");

        Assert.Equal(["Bills2"], Tabs(page));
        Assert.Equal([BaseUrl.Join($"records/{new string('2', 64)}.html"), BaseUrl.Join($"records/{new string('1', 64)}.html")],
            page.QuerySelectorAll(".record-row > div > a").Select(link => link.GetAttribute("href")!).ToArray());
        Assert.Contains("Pays landlords.", page.Body!.TextContent);
        Assert.Equal(["Introduced 3 Mar 2026"], page.QuerySelectorAll(".record-row-meta > span").Select(part => part.TextContent).Take(1).ToArray());
        Assert.Contains("2 bills and resolutions that Ann Able sponsored in this release", page.QuerySelector(".records-lead p")!.TextContent);
    }

    /// <summary>A revision that the backfill has not reached has no introduced date. The bills tab keeps it, prints no introduction date for it, and lists it after every dated bill.</summary>
    [Fact]
    public async Task TheBillsTabKeepsABillWithNoIntroducedDateAfterEveryDatedBillAndPrintsNoIntroductionDateForIt()
    {
        await using var host = await StartAsync();
        await BillsSeed.AddBillsAsync(host.OperationalDatabase,
            new SeededBill('1', "2026-03-02") { BillNumber = 1, Sponsorships = [new("p0", "Sponsor", "2026-01-10")] },
            new SeededBill('2', "2026-09-01") { BillNumber = 2, Undated = true, Sponsorships = [new("p0", "Sponsor", "2026-09-01")] });

        var page = await GetPageAsync(host, "/officials/p0/record.html?kind=bills");

        var rows = page.QuerySelectorAll(".record-row").ToArray();
        Assert.Equal([.. "12".Select(id => BaseUrl.Join($"records/{new string(id, 64)}.html"))], rows.Select(row => row.QuerySelector("a")!.GetAttribute("href")!).ToArray());
        Assert.Contains("Introduced 10 Jan 2026", rows[0].TextContent);
        Assert.DoesNotContain("Introduced", rows[1].TextContent);
        Assert.DoesNotContain(rows[1].QuerySelectorAll("span"), span => span.Children.Length == 0 && span.TextContent.Trim().Length == 0);
    }

    [Fact]
    public async Task TheBillsTabCountsAndListsEverySponsoredBillOfEachCongressOnceWithItsOwnStatusInSponsorshipOrder()
    {
        await using var host = await StartAsync();
        await BillsSeed.AddBillsAsync(host.OperationalDatabase,
            new SeededBill('1', "2026-03-02") { BillNumber = 1, Sponsorships = [new("p0", "Sponsor", "2026-01-10")] },
            new SeededBill('2', "2026-03-03") { BillNumber = 2, Sponsorships = [new("p0", "Sponsor", "2026-03-03"), new("p1", "Cosponsor", "2026-03-03")] },
            new SeededBill('3', "2026-03-03") { BillNumber = 3, Sponsorships = [new("p0", "Sponsor", "2026-03-03")] },
            new SeededBill('4', "2024-05-01") { Congress = 118, BillNumber = 4, Sponsorships = [new("p0", "Sponsor", "2024-05-01")] },
            new SeededBill('5', "2026-03-04") { BillNumber = 5, Sponsorships = [new("p1", "Sponsor", "2026-03-04"), new("p0", "Cosponsor", "2026-03-04")] },
            new SeededBill('6', "2026-03-05") { BillNumber = 6, FirstRelease = 2, Sponsorships = [new("p0", "Sponsor", "2026-03-05")] });
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            SET session_replication_role = replica;
            {PublicRecordPageRows.PinnedStatus(1, new string('2', 64), "Referred to the House Committee on Energy and Commerce.", "2026-03-05", 1)}
            {PublicRecordPageRows.PinnedStatus(1, new string('4', 64), "Referred to the House Committee on Ways and Means.", "2024-05-02", 1)}
            {PublicRecordPageRows.PinnedStatus(1, new string('5', 64), "Received in the Senate.", "2026-03-06", 1)}
            """);

        var page = await GetPageAsync(host, "/officials/p0/record.html?kind=bills");

        Assert.Equal(["Bills4"], Tabs(page));
        Assert.Contains("4 bills and resolutions that Ann Able sponsored in this release", page.QuerySelector(".records-lead p")!.TextContent);
        var rows = page.QuerySelectorAll(".record-row").ToArray();
        Assert.Equal([.. "2314".Select(id => BaseUrl.Join($"records/{new string(id, 64)}.html"))],
            rows.Select(row => row.QuerySelector("a")!.GetAttribute("href")!).ToArray());
        Assert.Equal(["In committee", "", "", "In committee"], rows.Select(row => row.QuerySelector(".badge")?.TextContent.Trim() ?? "").ToArray());
        Assert.Contains("Latest official action 5 Mar 2026", rows[0].TextContent);
        Assert.Contains("Latest official action 2 May 2024", rows[3].TextContent);
        Assert.DoesNotContain("Latest official action", rows[1].TextContent);
    }

    [Fact]
    public async Task TheStatementsTabListsTheStatementsThatNameTheOfficialAsASpeakerWithTheLabelOfTheViewAndTheirDates()
    {
        await using var host = await StartAsync();
        await StatementsSeed.AddAsync(host.OperationalDatabase, new SeededStatement('5', "2026-02-10", ["p0"], [], "Analysis", "Code"),
            new SeededStatement('6', null, ["p0"], [], "Person", "Person"), new SeededStatement('7', "2026-02-12", ["p1"], [], "Person", "Person"));

        var page = await GetPageAsync(host, "/officials/p0/record.html?kind=statements");

        Assert.Equal(["Statements2"], Tabs(page));
        var rows = page.QuerySelectorAll(".record-row").ToArray();
        Assert.Equal([BaseUrl.Join($"records/{new string('5', 64)}.html"), BaseUrl.Join($"records/{new string('6', 64)}.html")],
            rows.Select(row => row.QuerySelector("a")!.GetAttribute("href")!).ToArray());
        Assert.Contains("Said on 10 Feb 2026", rows[0].TextContent);
        Assert.Contains("Date not stated", rows[1].TextContent);
    }

    [Fact]
    public async Task APageThatEndsOnAnUndatedStatementContinuesWithTheOtherUndatedStatements()
    {
        await using var host = await StartAsync();
        await StatementsSeed.AddAsync(host.OperationalDatabase, [.. Enumerable.Range(0, 27)
            .Select(number => new SeededStatement('0', null, ["p0"], [], "Person", "Person") { RecordId = Id(number) })]);

        var first = await GetPageAsync(host, "/officials/p0/record.html?kind=statements");
        var next = first.QuerySelector(".pager a[rel=next]")!.GetAttribute("href")!;
        var second = await GetPageAsync(host, next.Replace("https://civic-lens.test/", "/"));

        var firstLinks = first.QuerySelectorAll(".record-row > div > a").Select(link => link.GetAttribute("href")!).ToArray();
        var secondLinks = second.QuerySelectorAll(".record-row > div > a").Select(link => link.GetAttribute("href")!).ToArray();
        Assert.Equal(25, firstLinks.Length);
        Assert.Equal(Enumerable.Range(0, 27).Select(number => BaseUrl.Join($"records/{Id(number)}.html")).ToArray(), firstLinks.Concat(secondLinks).ToArray());
        Assert.All(second.QuerySelectorAll(".record-row"), row => Assert.Contains("Date not stated", row.TextContent));
    }

    [Fact]
    public async Task TheExecutiveActionsTabListsTheActionsThePresidentSignedNewestFirstWithTheirKind()
    {
        await using var host = await StartAsync();
        await HomeSeed.AddExecutiveActionsAsync(host.OperationalDatabase,
            new SeededExecutiveAction('e', "ExecutiveOrder", "2026-05-20", "2026-05-21") { Title = "Newer order" },
            new SeededExecutiveAction('9', "Proclamation", "2026-05-18", "2026-05-19") { Title = "A proclamation" });
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, string.Join('\n', new[] { ('e', "ExecutiveOrder"), ('9', "Proclamation") }.Select(action => $"""
            SET session_replication_role = replica;
            INSERT INTO published_entries (kind, record_id, revision_number, executive_action_record_id, executive_action_revision_number, headline, document_type)
              VALUES ('executiveAction', '{new string(action.Item1, 64)}', 1, '{new string(action.Item1, 64)}', 1, 'H', '{action.Item2}');
            INSERT INTO published_entry_officials (kind, record_id, revision_number, "position", official_id)
              VALUES ('executiveAction', '{new string(action.Item1, 64)}', 1, 0, 'president');
            """)));

        var page = await GetPageAsync(host, "/officials/president/record.html");

        Assert.Equal(["Executive actions2"], Tabs(page));
        Assert.Equal(["Newer order", "A proclamation"], page.QuerySelectorAll(".record-row > div > a > span:first-child").Select(title => title.TextContent).ToArray());
        Assert.Equal(["Executive order", "Signed 20 May 2026"], page.QuerySelector(".record-row-meta")!.Children.Select(part => part.TextContent).ToArray());
    }

    [Fact]
    public async Task TheOfficesHeldFoldOfAPresidentNamesTheOfficeWordAndNeverTheRawExecutiveCode()
    {
        await using var host = await StartAsync();

        var page = await GetPageAsync(host, "/officials/president/record.html");

        Assert.Equal(["President, Republican, 20 Jan 2025 to Present"], page.QuerySelectorAll("#offices ul.record-rows > li").Select(item => item.TextContent).ToArray());
        Assert.DoesNotContain(ExecutiveRosterParser.President, page.QuerySelector("#offices")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRecordSectionHasTheAccessibleNameTheRecordOfTheOfficial()
    {
        await using var host = await StartAsync();
        await StatementsSeed.AddAsync(host.OperationalDatabase, new SeededStatement('5', "2026-02-10", ["p0"], [], "Analysis", "Code"));

        var page = await GetPageAsync(host, "/officials/p0/record.html");

        Assert.Equal("The record of Ann Able", Assert.Single(page.QuerySelectorAll("section.record-section")).GetAttribute("aria-label"));
    }

    [Fact]
    public async Task TheDefaultTabIsTheFirstKindWithRecordsAndAKindWithNoRecordsFallsBackToIt()
    {
        await using var host = await StartAsync();
        await StatementsSeed.AddAsync(host.OperationalDatabase, new SeededStatement('5', "2026-02-10", ["p0"], [], "Analysis", "Code"));

        var plain = await GetPageAsync(host, "/officials/p0/record.html");
        var asked = await GetPageAsync(host, "/officials/p0/record.html?kind=bills");

        Assert.Equal("Statements1", Tabs(plain).Single());
        Assert.Equal("true", plain.QuerySelector(".segmented-item")!.GetAttribute("aria-current"));
        Assert.Equal(Tabs(plain), Tabs(asked));
        Assert.Single(asked.QuerySelectorAll(".record-row"));
    }

    [Fact]
    public async Task AnOfficialWithNoRecordsAnswers200WithTheEmptyStateAndNoTabsAndStillShowsTheOffices()
    {
        await using var host = await StartAsync();

        var page = await GetPageAsync(host, "/officials/p1/record.html");

        Assert.Contains("No records yet", page.QuerySelector(".empty")!.TextContent);
        Assert.Empty(page.QuerySelectorAll(".segmented-item"));
        Assert.Null(page.QuerySelector(".pager"));
        Assert.Contains("Offices held (1 term)", page.QuerySelector("#offices summary")!.TextContent);
    }

    [Fact]
    public async Task OfficesHeldListsEachDatedTermOldestFirstWithItsPartyAndAnOfficialWithNoTermHasNoFold()
    {
        await using var host = await StartAsync();

        var held = await GetPageAsync(host, "/officials/p0/record.html");
        var none = await GetPageAsync(host, "/officials/bare/record.html");

        Assert.Equal("Offices held (2 terms)", held.QuerySelector("#offices summary")!.TextContent);
        Assert.Equal(["Representative for MA, district 7, Democrat, 3 Jan 2021 to 3 Jan 2023", "Senator for MA, Democrat, 3 Jan 2023 to Present"],
            held.QuerySelectorAll("#offices ul.record-rows > li").Select(item => item.TextContent).ToArray());
        Assert.Null(none.QuerySelector("#offices"));
        Assert.Equal("All records of Cy Cole", none.QuerySelector("h1")!.TextContent);
    }

    [Theory]
    [InlineData("/officials/nobody/record.html", HttpStatusCode.NotFound)]
    [InlineData("/officials/bad%20id/record.html", HttpStatusCode.NotFound)]
    [InlineData("/officials/p0/record.html?kind=letters", HttpStatusCode.BadRequest)]
    [InlineData("/officials/p0/record.html?cursor=nonsense", HttpStatusCode.BadRequest)]
    public async Task AnUnknownOfficialAnswers404AndABadKindOrCursorAnswers400(string path, HttpStatusCode expected)
    {
        await using var host = await StartAsync();

        using var response = await host.Client.GetAsync(path);

        Assert.Equal(expected, response.StatusCode);
    }
}
