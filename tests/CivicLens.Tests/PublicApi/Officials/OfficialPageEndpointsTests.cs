using System.Net;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Home;
using CivicLens.Tests.PublicApi.Statements;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Officials;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class OfficialPageEndpointsTests(PostgresCollection postgres)
{
    private static readonly PublicBaseUrl BaseUrl = PublicBaseUrl.Parse("https://civic-lens.test", "test")!;
    private static readonly string Linked = new('5', 64);
    private static readonly string Unlinked = new('6', 64);

    private static readonly SeededOfficial[] Officials =
    [
        new("p0", "Ann Able", new SeededTerm("Representative", "Representative", "2025-01-03") { State = "MA", District = 7, Party = "Democrat", EndsOn = "2027-01-03" }),
        new("p1", "Ben Baker", new SeededTerm("Representative", "Representative", "2025-01-03") { State = "TX", District = 3, Party = "Republican" }),
        new("p2", "Cy Cole", new SeededTerm("Representative", "Representative", "2025-01-03") { State = "MA", District = 1, Party = "Democrat" }),
        new("president", "Eve East", new SeededTerm("President", "Executive", "2025-01-20") { Party = "Republican" }),
        new("justice", "Jo Judge", new SeededTerm("Associate Justice", "Judicial", "2020-10-27"))
    ];

    // p0 votes against the Democratic majority on a (split), misses b, votes with the party on c (split), and breaks on d, which is not a split vote.
    private static readonly SeededVote[] Votes =
    [
        new('a', "2026-02-01") { Casts = ["Nay", "Nay", "Yea"], PersonIds = ["p0", "p1", "p2"], RollCall = 1 },
        new('b', "2026-02-03") { Casts = ["Not Voting", "Nay", "Yea"], PersonIds = ["p0", "p1", "p2"], RollCall = 2 },
        new('c', "2026-02-05") { Casts = ["Yea", "Nay", "Yea"], PersonIds = ["p0", "p1", "p2"], RollCall = 3 },
        new('d', "2026-02-06") { Casts = ["Nay", "Nay", "Yea"], PersonIds = ["p0", "p1", "p2"], RollCall = 4 }
    ];

    private async Task<ApiHost> StartAsync(bool storeBillCounts = true, bool withBrowserApi = false)
    {
        var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite(), options => options with
        {
            PublicBaseUrl = BaseUrl,
            BrowserApiUrl = withBrowserApi ? PublicBaseUrl.Parse("https://api.civic-lens.test", "test") : null
        });
        var database = host.OperationalDatabase;
        await ViewsSeed.ActivateReleaseAsync(database, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await OfficialsSeed.AddOfficialsAsync(database, Officials);
        await ViewsSeed.AddVotesAsync(database, Votes);
        await ViewsSeed.ExecuteAsync(database, $"""
            SET session_replication_role = replica;
            UPDATE vote_member_list_entries SET party = CASE person_id WHEN 'p0' THEN 'Democrat' WHEN 'p1' THEN 'Republican' WHEN 'p2' THEN 'Democrat' END;
            UPDATE vote_revisions SET columns_written = true, alignment = 'Split', democrat_majority = 'Yea', republican_majority = 'Nay'
              WHERE record_id IN ('{new string('a', 64)}', '{new string('b', 64)}', '{new string('c', 64)}');
            UPDATE vote_revisions SET columns_written = true, alignment = 'PartiesAgree', democrat_majority = 'Yea', republican_majority = 'Yea'
              WHERE record_id = '{new string('d', 64)}';
            """);
        await StatementsSeed.AddAsync(database, new SeededStatement('5', "2026-02-10", ["p0"], [], "Analysis", "Code"),
            new SeededStatement('6', "2026-02-11", ["p0"], [], "Person", "Person"));
        await ViewsSeed.ExecuteAsync(database, $"""
            SET session_replication_role = replica;
            {PublicRecordPageRows.Link(new string('7', 64), Linked, new string('c', 64), "vote", "StatementAboutVote", "2026-02-10")}
            {PublicRecordPageRows.RelationshipRevision(new string('7', 64), 1, Linked, new string('c', 64), "vote", "StatementAboutVote", "The statement names this vote.")}
            """);
        await BillsSeed.AddBillsAsync(database,
            new SeededBill('1', "2026-03-02") { BillNumber = 1, Sponsorships = [new("p0", "Sponsor", "2026-01-10")] },
            new SeededBill('2', "2026-03-03") { BillNumber = 2, BillLine = "Pays landlords.", Sponsorships = [new("p0", "Sponsor", "2026-03-03"), new("p1", "Cosponsor", "2026-03-03")] },
            new SeededBill('3', "2026-03-04") { BillNumber = 3, Sponsorships = [new("p1", "Sponsor", "2026-03-04")] });
        if (storeBillCounts) await StoredBillCounts.FillAsync(database, 1, 119);
        return host;
    }

    private static async Task<IDocument> GetPageAsync(ApiHost host, string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await host.Client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{path} gave {(int)response.StatusCode}: {body}");
        return await new HtmlParser().ParseDocumentAsync(body);
    }

    [Fact]
    public async Task TheOwnWebsiteOfTheOfficialShowsAsALinkNamedByItsHostAndIsAbsentWhenTheReleaseHoldsNone()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica; UPDATE release_officials SET website = 'https://able.house.gov/about' WHERE official_id = 'p0';");

        var withSite = await GetPageAsync(host, "/web/officials/p0");
        var withoutSite = await GetPageAsync(host, "/web/officials/p1");

        var link = withSite.QuerySelector(".official-head a[href='https://able.house.gov/about']")!;
        Assert.Equal("able.house.gov", link.TextContent);
        Assert.Equal("Official site: able.house.gov.", link.ParentElement!.TextContent);
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
        Assert.Null(withoutSite.QuerySelector(".official-head a"));
    }

    [Fact]
    public async Task AStoredWebsiteThatIsNotAValidHttpsAddressFailsThePageInsteadOfBecomingALink()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica; UPDATE release_officials SET website = 'javascript:alert(1)' WHERE official_id = 'p0';");

        using var response = await host.Client.GetAsync("/web/officials/p0");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task TheTenureSentenceFollowsTheRoleAndComesBeforeTheTermEndAndAnOfficialWithNoHouseOrSenateTermHasNone()
    {
        await using var host = await StartAsync();

        var member = await GetPageAsync(host, "/web/officials/p0");
        var justice = await GetPageAsync(host, "/web/officials/justice");

        Assert.Equal("Democratic representative for Massachusetts, district 7. In the House since January 2025. Current term ends 3 Jan 2027.",
            member.QuerySelector(".official-lede")!.TextContent);
        Assert.DoesNotContain("since", justice.QuerySelector(".official-lede")!.TextContent);
    }

    [Fact]
    public async Task TheCompareSectionIsAHiddenSlotForTheRememberedZipAndOnlyWhenTheBrowserApiOriginIsConfigured()
    {
        await using var host = await StartAsync(withBrowserApi: true);
        await using var withoutApi = await StartAsync();

        var page = await GetPageAsync(host, "/web/officials/p0");
        var plain = await GetPageAsync(withoutApi, "/web/officials/p0");

        var section = page.QuerySelector("section#your-members")!;
        Assert.True(section.HasAttribute("hidden"));
        Assert.Equal("Compare with your other members", section.QuerySelector("h2")!.TextContent);
        var slot = section.QuerySelector("[data-official-members]")!;
        Assert.Equal("p0", slot.GetAttribute("data-official-members"));
        Assert.Equal("https://api.civic-lens.test", slot.GetAttribute("data-members-api"));
        Assert.Equal(0, slot.ChildElementCount);
        Assert.Null(plain.QuerySelector("#your-members"));
    }

    [Fact]
    public async Task TheStripLinksOnlyThePartyBreakAndTheMissedVoteNewestFirstAndMatchesTheFigures()
    {
        await using var host = await StartAsync();

        var page = await GetPageAsync(host, "/web/officials/p0");

        var marks = page.QuerySelectorAll(".official-strip a").Select(link => (link.GetAttribute("href")!, link.ClassList.Contains("missed"))).ToArray();
        Assert.Equal([(BaseUrl.Join($"records/{new string('b', 64)}.html"), true), (BaseUrl.Join($"records/{new string('a', 64)}.html"), false)], marks);
        var figures = string.Join('|', page.QuerySelectorAll(".official-figs li").Select(item => item.TextContent));
        Assert.Contains("3roll calls voted on, of 4 House roll calls", figures);
        Assert.Contains("1of 2 split votes", figures);
        Assert.Contains("1 vote against most House Democrats", page.QuerySelector(".official-legend")!.TextContent);
        Assert.Contains("1 missed House roll call", page.QuerySelector(".official-legend")!.TextContent);
    }

    [Fact]
    public async Task NoStripSquareTextNamesAVoteHeadlineBecauseTheHeadlineIsAiText()
    {
        await using var host = await StartAsync();

        var page = await GetPageAsync(host, "/web/officials/p0");

        var headlines = page.QuerySelectorAll("#votes .shared-vote-row h3 a").Select(link => link.TextContent).ToArray();
        Assert.NotEmpty(headlines);
        var strip = page.QuerySelector(".official-strip")!;
        Assert.All(headlines, headline => Assert.DoesNotContain(headline, strip.TextContent));
        Assert.All(strip.QuerySelectorAll("a"), mark => Assert.Equal(mark.GetAttribute("title"), mark.QuerySelector(".visually-hidden")!.TextContent));
    }

    [Fact]
    public async Task TheStripAndTheFigureCountTheSameVotesWhenAMemberHasAThirdPartyATieOrAPresentCast()
    {
        await using var host = await StartAsync();
        var database = host.OperationalDatabase;
        await ViewsSeed.AddVotesAsync(database,
            new SeededVote('e', "2026-02-07") { Casts = ["Nay"], PersonIds = ["p0"], RollCall = 5 },
            new SeededVote('f', "2026-02-08") { Casts = ["Nay"], PersonIds = ["p0"], RollCall = 6 },
            new SeededVote('9', "2026-02-09") { Casts = ["Present"], PersonIds = ["p0"], RollCall = 7 });
        await ViewsSeed.ExecuteAsync(database, $"""
            SET session_replication_role = replica;
            UPDATE vote_member_list_entries SET party = 'Independent' WHERE person_id = 'p0' AND list_id = (SELECT member_list_id FROM vote_revisions WHERE record_id = '{new string('e', 64)}');
            UPDATE vote_member_list_entries SET party = 'Democrat' WHERE person_id = 'p0' AND list_id IN (SELECT member_list_id FROM vote_revisions WHERE record_id IN ('{new string('f', 64)}', '{new string('9', 64)}'));
            UPDATE vote_revisions SET columns_written = true, alignment = 'Split', democrat_majority = 'Yea', republican_majority = 'Nay'
              WHERE record_id IN ('{new string('e', 64)}', '{new string('9', 64)}');
            UPDATE vote_revisions SET columns_written = true, alignment = 'Split', democrat_majority = NULL, republican_majority = NULL
              WHERE record_id = '{new string('f', 64)}';
            """);

        var page = await GetPageAsync(host, "/web/officials/p0");

        Assert.Equal(2, page.QuerySelectorAll(".official-strip a").Length);
        Assert.Equal(1, page.QuerySelectorAll(".official-strip a.against").Length);
        Assert.Contains("1of 2 split votes", string.Join('|', page.QuerySelectorAll(".official-figs li").Select(item => item.TextContent)));
        Assert.Contains("1 vote against most House Democrats", page.QuerySelector(".official-legend")!.TextContent);
    }

    [Fact]
    public async Task RecentVotesShowTheCastOfTheOfficialAndOnlyLinkedStatementsAppearWithTheirVote()
    {
        await using var host = await StartAsync();

        var page = await GetPageAsync(host, "/web/officials/p0");

        Assert.Equal(4, page.QuerySelectorAll("#votes .shared-vote-row").Length);
        Assert.Contains("No (Nay)", page.QuerySelector("#votes .shared-vote-row")!.TextContent);
        var statements = Assert.Single(page.QuerySelectorAll("#statements .official-rows li"));
        Assert.Contains("Said on 10 Feb 2026", statements.TextContent);
        Assert.Contains("House roll call 3", statements.QuerySelector(".official-related a")!.TextContent);
        Assert.Contains("Yes (Yea)", statements.QuerySelector(".shared-vote-cast-value")!.TextContent);
        Assert.Contains(PublishedRecordLabels.StatementModelWording, statements.TextContent);
        Assert.Equal(2, statements.QuerySelectorAll(".official-source-label[data-origin=model]").Length);
        Assert.Equal(4, page.QuerySelectorAll("#votes .shared-vote-label").Length);
        Assert.All(page.QuerySelectorAll("#votes .shared-vote-label"), label => Assert.Equal(PublishedRecordLabels.ModelWording, label.TextContent.Trim()));
        Assert.DoesNotContain(page.QuerySelectorAll("#statements a"), link => link.GetAttribute("href")!.Contains(Unlinked));
    }

    [Theory]
    [InlineData("Code", "Code checked this link")]
    [InlineData("Person", "A reviewer checked this link")]
    [InlineData(null, null)]
    public async Task EachLinkedStatementNamesWhoCheckedItsLinkAndNoLabelShowsBeforeTheBackfill(string? approver, string? label)
    {
        await using var host = await StartAsync();
        if (approver is not null)
            await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
                SET session_replication_role = replica;
                UPDATE published_entries SET approved_by = '{approver}' WHERE kind = 'relationship' AND record_id = '{new string('7', 64)}';
                """);

        var page = await GetPageAsync(host, "/web/officials/p0");

        var row = Assert.Single(page.QuerySelectorAll("#statements .official-rows li"));
        Assert.Equal(label, row.QuerySelector(".link-checker")?.TextContent);
        Assert.DoesNotContain("A reviewer approved each link.", page.QuerySelector("#statements")!.TextContent);
        Assert.Contains("A link does not mean the vote or bill matches the statement.", page.QuerySelector("#statements")!.TextContent);
    }

    [Fact]
    public async Task BillRowsListOnlyBillsTheOfficialSponsoredNewestFirstWithIntroducedDatesAndTheBillLine()
    {
        await using var host = await StartAsync();

        var page = await GetPageAsync(host, "/web/officials/p0");

        var rows = page.QuerySelectorAll("#bills .official-rows li");
        Assert.Equal(2, rows.Length);
        Assert.Equal("Pays landlords.", rows[0].QuerySelector(".bill-line")!.TextContent);
        Assert.Null(rows[1].QuerySelector(".bill-line"));
        Assert.Single(page.QuerySelectorAll("#bills > p.muted"));
        Assert.Contains("Introduced 3 Mar 2026", rows[0].QuerySelector(".official-meta")!.TextContent);
        Assert.Contains("Introduced 10 Jan 2026", rows[1].QuerySelector(".official-meta")!.TextContent);
        Assert.Null(page.QuerySelector("#bills .official-more"));
    }

    [Fact]
    public async Task BillRowsShowEachSponsoredBillOfTheCongressOnceWithItsOwnStatusAndSkipOtherCongressesAndOtherSponsors()
    {
        await using var host = await StartAsync();
        await BillsSeed.AddBillsAsync(host.OperationalDatabase,
            new SeededBill('4', "2024-05-01") { Congress = 118, BillNumber = 4, Sponsorships = [new("p0", "Sponsor", "2026-04-02")] },
            new SeededBill('8', "2026-04-01") { BillNumber = 8, Sponsorships = [new("p0", "Sponsor", "2026-04-01"), new("p2", "Cosponsor", "2026-04-01")] },
            new SeededBill('9', "2026-04-03") { BillNumber = 9, Sponsorships = [new("p2", "Sponsor", "2026-04-03"), new("p0", "Cosponsor", "2026-04-03")] });
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            SET session_replication_role = replica;
            {PublicRecordPageRows.PinnedStatus(1, new string('1', 64), "Referred to the House Committee on Energy and Commerce.", "2026-01-12", 1)}
            {PublicRecordPageRows.PinnedStatus(1, new string('4', 64), "Received in the Senate.", "2024-05-02", 1)}
            {PublicRecordPageRows.PinnedStatus(1, new string('9', 64), "Received in the Senate.", "2026-04-04", 1)}
            """);

        var page = await GetPageAsync(host, "/web/officials/p0");

        var rows = page.QuerySelectorAll("#bills .official-rows li").ToArray();
        Assert.Equal([.. "821".Select(id => BaseUrl.Join($"records/{new string(id, 64)}.html"))],
            rows.Select(row => row.QuerySelector("a")!.GetAttribute("href")!).ToArray());
        Assert.Equal(["", "", "In committee"], rows.Select(row => row.QuerySelector(".badge")?.TextContent.Trim() ?? "").ToArray());
        Assert.Contains("Latest official action 12 Jan 2026", rows[2].QuerySelector(".official-meta")!.TextContent);
        Assert.Equal("See all 4 bills", page.QuerySelector("#bills .official-more a")!.TextContent);
    }

    [Fact]
    public async Task TheSeeAllBillsLinkCountsEveryCongressOfTheReleaseAsTheBillsTabDoesAndNotTheBillFigureOfOneCongress()
    {
        await using var host = await StartAsync();
        await BillsSeed.AddBillsAsync(host.OperationalDatabase,
            new SeededBill('4', "2024-05-01") { Congress = 118, BillNumber = 4, Sponsorships = [new("p0", "Sponsor", "2024-05-01")] });

        var page = await GetPageAsync(host, "/web/officials/p0");

        Assert.Equal(2, page.QuerySelectorAll("#bills .official-rows li").Length);
        var link = page.QuerySelector("#bills .official-more a")!;
        Assert.Equal("See all 3 bills", link.TextContent);
        Assert.Equal(BaseUrl.Join("officials/p0/record.html?kind=bills"), link.GetAttribute("href"));
    }

    [Fact]
    public async Task KnownIssuesRemainVisibleWhenTheBillCountSnapshotIsUnavailable()
    {
        await using var host = await StartAsync(storeBillCounts: false);
        await BillsSeed.AddBillsAsync(host.OperationalDatabase,
            new SeededBill('4', "2026-03-05") { BillNumber = 4, Issues = ["housing"], Sponsorships = [new("p0", "Sponsor", "2026-03-05")] });
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase,
            "INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (1, 'housing', 'Housing', 'Housing policy');");
        using var detailResponse = await host.Client.GetAsync("/v1/officials/p0");
        detailResponse.EnsureSuccessStatusCode();
        using var detail = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, detail.RootElement.GetProperty("bills").ValueKind);
        Assert.NotEmpty(detail.RootElement.GetProperty("issues").GetProperty("issues").EnumerateArray());

        var page = await GetPageAsync(host, "/web/officials/p0");

        var issues = page.QuerySelector("#issues");
        Assert.NotNull(issues);
        Assert.Contains("119th Congress", issues.TextContent);
        Assert.Equal("Housing 1", Assert.Single(issues.QuerySelectorAll(".official-chips a")).TextContent);
        Assert.Empty(page.QuerySelectorAll("a[href='#bills']"));
        Assert.Contains(OfficialPageText.BillCountsUnavailable, page.QuerySelector(".official-figs")!.TextContent);
    }

    [Fact]
    public async Task AnOfficialWithNoCongressShowsNoBillCaptionEvenWhenTheReleaseHasNoBillCountSnapshot()
    {
        await using var host = await StartAsync(storeBillCounts: false);

        var page = await GetPageAsync(host, "/web/officials/justice");

        Assert.Equal("Jo Judge", page.QuerySelector("h1")!.TextContent);
        Assert.DoesNotContain(OfficialPageText.BillCountsUnavailable, page.Body!.TextContent);
    }

    [Fact]
    public async Task PersonApprovalGivesAModelWrittenStatementTheCheckedByPersonLabel()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase,
            $"UPDATE published_entries SET approved_by = 'Person' WHERE kind = 'statement' AND record_id = '{Linked}'");

        var page = await GetPageAsync(host, "/web/officials/p0");

        var labels = page.QuerySelectorAll("#statements .official-source-label[data-origin=model]");
        Assert.Equal(2, labels.Length);
        Assert.Contains(labels, label => label.TextContent == PublishedRecordLabels.StatementCheckedByPersonWording);
        Assert.DoesNotContain(labels, label => label.TextContent == PublishedRecordLabels.StatementModelWording);
    }

    [Fact]
    public async Task LinkedCodeVoteKeepsItsCodeLabelWithoutAnAiBadge()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase,
            $"UPDATE vote_revisions SET text_origin = 'Code' WHERE record_id = '{new string('c', 64)}'");

        var page = await GetPageAsync(host, "/web/officials/p0");

        Assert.Single(page.QuerySelectorAll("#statements .official-source-label[data-origin=model]"));
        Assert.Equal(PublishedRecordLabels.CodeWording,
            Assert.Single(page.QuerySelectorAll("#statements .official-source-label[data-origin=code]")).TextContent);
    }

    [Fact]
    public async Task AnOfficialWithNoLinkedStatementsHidesThatSectionAndKeepsTheMessageInTheJumpRow()
    {
        await using var host = await StartAsync();

        var page = await GetPageAsync(host, "/web/officials/p2");

        Assert.Null(page.QuerySelector("#statements"));
        Assert.Equal(OfficialPageText.NoLinks, page.QuerySelector(".official-jump p")!.TextContent);
        Assert.Null(page.QuerySelector("#bills"));
    }

    [Fact]
    public async Task AStatementLinkedOnlyToABillKeepsTheNoLinksMessageHidden()
    {
        await using var host = await StartAsync();
        await StatementsSeed.AddAsync(host.OperationalDatabase, new SeededStatement('4', "2026-02-12", ["p2"], [], "Person", "Person"));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            SET session_replication_role = replica;
            {PublicRecordPageRows.Link(new string('8', 64), new string('4', 64), new string('1', 64), "action", "StatesPositionOn", "2026-02-12")}
            {PublicRecordPageRows.RelationshipRevision(new string('8', 64), 1, new string('4', 64), new string('1', 64), "action", "StatesPositionOn", "The statement names this bill.")}
            """);

        var page = await GetPageAsync(host, "/web/officials/p2");

        Assert.Null(page.QuerySelector("#statements"));
        Assert.Null(page.QuerySelector(".official-jump p"));
    }

    [Fact]
    public async Task ThePageCarriesTheReleaseETagTheMembersStripSlotAndTheSharedTrail()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.GetAsync("/web/officials/p0");
        var page = await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());

        Assert.Matches("^W/\"r1-", response.Headers.ETag!.ToString());
        Assert.Equal(["Home", "Officials"], page.QuerySelectorAll("nav[aria-label=Breadcrumb] a").Select(link => link.TextContent).ToArray());
        Assert.Equal("officials/p0.html", new Uri(page.QuerySelector("link[rel=canonical]")!.GetAttribute("href")!).AbsolutePath.TrimStart('/'));
    }

    [Fact]
    public async Task APresidentGetsPerTypeSectionsWithCountsAndNewestFirstRowsAndNoSpeeches()
    {
        await using var host = await StartAsync();
        await HomeSeed.AddExecutiveActionsAsync(host.OperationalDatabase,
            new SeededExecutiveAction('e', "ExecutiveOrder", "2026-05-20", "2026-05-21") { Title = "Newer order" },
            new SeededExecutiveAction('f', "ExecutiveOrder", "2026-05-01", "2026-05-02") { Title = "Older order" },
            new SeededExecutiveAction('9', "Proclamation", "2026-05-18", "2026-05-19") { Title = "A proclamation" });
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, string.Join('\n', new[] { ('e', "ExecutiveOrder"), ('f', "ExecutiveOrder"), ('9', "Proclamation") }.Select(action => $"""
            SET session_replication_role = replica;
            INSERT INTO published_entries (kind, record_id, revision_number, executive_action_record_id, executive_action_revision_number, headline, document_type)
              VALUES ('executiveAction', '{new string(action.Item1, 64)}', 1, '{new string(action.Item1, 64)}', 1, 'H', '{action.Item2}');
            INSERT INTO published_entry_officials (kind, record_id, revision_number, "position", official_id)
              VALUES ('executiveAction', '{new string(action.Item1, 64)}', 1, 0, 'president');
            """)));

        var page = await GetPageAsync(host, "/web/officials/president");

        Assert.Equal("President Eve East", page.QuerySelector("h1")!.TextContent);
        Assert.Contains("3 executive actions", page.QuerySelector(".official-lede")!.TextContent);
        Assert.Contains("signed from 1 May 2026 to 20 May 2026", page.QuerySelector(".official-lede")!.TextContent);
        Assert.Equal(["Executive orders (2)", "Proclamations (1)"], page.QuerySelectorAll(".official-exec h2").Select(heading => heading.TextContent).ToArray());
        Assert.Equal(["Newer order", "Older order"], page.QuerySelectorAll("#type-executiveorder .official-row-title").Select(link => link.TextContent).ToArray());
        Assert.Null(page.QuerySelector("#votes"));
        Assert.DoesNotContain("What the President said", page.Body!.TextContent);
    }

    [Theory]
    [InlineData("/web/officials/nobody")]
    [InlineData("/web/officials/bad%20id")]
    public async Task AnUnknownOfficialAnswersWithTheNotFoundPage(string path)
    {
        await using var host = await StartAsync();

        using var response = await host.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
