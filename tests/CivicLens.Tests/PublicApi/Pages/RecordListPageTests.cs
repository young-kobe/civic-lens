using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicApi;
using CivicLens.PublicApi.Bills;
using CivicLens.PublicApi.Pages;
using CivicLens.PublicApi.Votes;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Officials;
using CivicLens.Tests.PublicApi.Statements;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Pages;

/// <summary>The gated per-kind list pages under /web: votes, bills, statements, executive actions, and document changes.</summary>
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

    // ---- shared frame: tabs, header, count line ----

    [Fact]
    public async Task EveryListPageNamesItsKindInTheTabsAndTheHeaderLinksOfThePreviewLeadToTheLists()
    {
        await using var host = await StartAsync();
        string[] paths = ["/web/votes", "/web/bills", "/web/statements", "/web/executive-actions", "/web/document-changes"];

        foreach (var path in paths)
        {
            var page = await GetPageAsync(host, path);
            var tabs = page.QuerySelectorAll("nav[aria-label='Lists of records'] a").Select(tab => (tab.TextContent.Trim(), tab.GetAttribute("href"))).ToArray();
            Assert.Equal([("Votes", "https://civic-lens.test/web/votes"), ("Bills", "https://civic-lens.test/web/bills"), ("Statements", "https://civic-lens.test/web/statements"),
                ("Executive actions", "https://civic-lens.test/web/executive-actions"), ("Document changes", "https://civic-lens.test/web/document-changes")], tabs);
            Assert.Equal(path, "/" + new Uri(page.QuerySelector("nav[aria-label='Lists of records'] a[aria-current]")!.GetAttribute("href")!).AbsolutePath.TrimStart('/'));
            var header = page.QuerySelectorAll("nav.public-nav a").ToDictionary(link => link.TextContent.Trim(), link => link.GetAttribute("href"));
            Assert.Equal("https://civic-lens.test/web/votes", header["Votes"]);
            Assert.Equal("https://civic-lens.test/web/bills", header["Bills"]);
            Assert.Equal("https://civic-lens.test/web/executive-actions", header["Executive actions"]);
        }
    }

    [Fact]
    public async Task ANoReleaseAnswers503AnInvalidFilterAnswers400AndACursorOfAnotherReleaseRedirectsToTheFirstPage()
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite(), options => options with { PublicBaseUrl = BaseUrl });
        foreach (var path in new[] { "/web/votes", "/web/bills", "/web/statements", "/web/executive-actions", "/web/document-changes" })
            await GetPageAsync(host, path, HttpStatusCode.ServiceUnavailable);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);

        await GetPageAsync(host, "/web/votes?chamber=moon", HttpStatusCode.BadRequest);
        await GetPageAsync(host, "/web/votes?bill=nonsense", HttpStatusCode.BadRequest);
        await GetPageAsync(host, "/web/bills?from=2026-02-01&to=2026-01-01", HttpStatusCode.BadRequest);
        await GetPageAsync(host, "/web/executive-actions?type=law", HttpStatusCode.BadRequest);
        var stale = new VoteCursor(99, new DateOnly(2026, 3, 1), PublishedChamber.House, 1, 1, new string('a', 64)).Encode();
        using var noRedirect = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = host.Client.BaseAddress };
        var response = await noRedirect.GetAsync($"/web/votes?chamber=house&cursor={stale}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/web/votes?chamber=house", response.Headers.Location!.OriginalString);
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
        Assert.Equal("Showing 2 votes on this page. Release 1.", page.QuerySelector(".list-status")!.TextContent);
        Assert.Contains("House and Senate roll call votes in this release", page.QuerySelector(".list-scope")!.TextContent);
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
        Assert.Null(empty.QuerySelector(".list-status"));
    }

    // ---- bills ----

    [Fact]
    public async Task TheBillListShowsSponsorLatestActionStatusAndTheAiLabelOnlyWhenTheViewHasOne()
    {
        await using var host = await StartAsync();
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase, new SeededOfficial("p1", "Ann Able"));
        await BillsSeed.AddBillsAsync(host.OperationalDatabase,
            new SeededBill('a', "2026-03-02") { Sponsorships = [new("p1", "Sponsor", "2026-03-02")] },
            new SeededBill('b', "2026-03-03") { TextOrigin = "Person", BillNumber = 8, Sponsorships = [new("p2", "Sponsor", "2026-03-03")] });
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;\n"
            + PublicRecordPageRows.PinnedStatus(1, new string('a', 64), "Referred to the House Committee on Energy and Commerce.", "2026-02-13", PublishedAt.UtcTicks));

        var page = await GetPageAsync(host, "/web/bills");

        var (person, model) = (Rows(page)[0], Rows(page)[1]);
        Assert.Null(Label(person));
        Assert.Equal(ModelLabel, Label(model));
        Assert.DoesNotContain("Sponsor:", Text(person));
        Assert.Contains("Sponsor: Ann Able", Text(model));
        Assert.Contains("Latest sponsorship 2 Mar 2026", Text(model));
        Assert.Contains("Latest official action 13 Feb 2026: In committee: sent to the House Committee on Energy and Commerce.", Text(model));
        Assert.Equal("In committee", model.QuerySelector(".badge")!.TextContent.Trim());
        Assert.Null(person.QuerySelector(".badge"));
        Assert.Equal("Showing 2 bills and resolutions on this page. Release 1.", page.QuerySelector(".list-status")!.TextContent);
    }

    [Fact]
    public async Task TheBillListFiltersByIssuePolicyAreaBillNumberAndDateAndPagesByTwentyFive()
    {
        await using var host = await StartAsync();
        await BillsSeed.AddBillsAsync(host.OperationalDatabase,
            new SeededBill('a', "2026-03-01") { Issues = ["health"], BillNumber = 4 },
            new SeededBill('b', "2026-03-02") { BillType = "s", BillNumber = 4, PolicyAreaId = "tax" },
            new SeededBill('c', "2026-03-03") { BillNumber = 9 });

        Assert.Equal(["a"], Ids(await GetPageAsync(host, "/web/bills?issue=health")));
        Assert.Equal(["b"], Ids(await GetPageAsync(host, "/web/bills?policyArea=tax")));
        Assert.Equal(["b"], Ids(await GetPageAsync(host, "/web/bills?bill=S.%204")));
        Assert.Equal(["a", "b"], Ids(await GetPageAsync(host, "/web/bills?bill=4")).Order());
        Assert.Equal(["c"], Ids(await GetPageAsync(host, "/web/bills?from=2026-03-03")));
        var filtered = await GetPageAsync(host, "/web/bills?policyArea=tax");
        Assert.Equal("tax", filtered.QuerySelector("select[name=policyArea] option[selected]")!.GetAttribute("value"));
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

    [Fact]
    public async Task TheStatementListShowsSpeakersAndDateOrNoDateAndTheAiLabelOnlyWhenTheViewHasOne()
    {
        await using var host = await StartAsync();
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase, new SeededOfficial("official-a", "Ann Able"), new SeededOfficial("official-z", "Zed Zane"));
        await StatementsSeed.AddAsync(host.OperationalDatabase,
            new('a', "2026-03-02", ["official-z", "official-a"], [], "Analysis", "Code"),
            new('b', null, ["official-a"], [], "Person", "Person"));

        var page = await GetPageAsync(host, "/web/statements");

        Assert.Equal(["Headline a", "Headline b"], Titles(page));
        Assert.NotNull(page.QuerySelectorAll(".record-row")[0].QuerySelectorAll("a > span").FirstOrDefault(span => span.TextContent.StartsWith("An AI summary")));
        Assert.Equal(2, page.QuerySelectorAll(".record-row")[0].QuerySelectorAll("a > span").Length - 1);
        Assert.Equal(2, page.QuerySelectorAll(".record-row")[1].QuerySelectorAll("a > span").Length - 1 + 1);
        Assert.Contains("Zed Zane, Ann Able, Said on 2 Mar 2026", Text(Rows(page)[0]));
        Assert.Contains("Ann Able, Date not stated", Text(Rows(page)[1]));
        Assert.Equal("Showing 2 statements on this page. Release 1.", page.QuerySelector(".list-status")!.TextContent);
    }

    [Fact]
    public async Task TheStatementListFiltersByOfficialIssueAndDateAndNamesTheOfficialInTheCountLine()
    {
        await using var host = await StartAsync();
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase, new SeededOfficial("official-a", "Ann Able"), new SeededOfficial("official-z", "Zed Zane"));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (1, 'health', 'Health care', 'Defined');");
        await StatementsSeed.AddAsync(host.OperationalDatabase,
            new('a', "2026-03-02", ["official-z"], ["health"], "Person", "Person"),
            new('b', "2026-03-05", ["official-a"], [], "Person", "Person"),
            new('c', null, ["official-a"], [], "Person", "Person"));

        Assert.Equal(["Headline b", "Headline c"], Titles(await GetPageAsync(host, "/web/statements?official=official-a")));
        Assert.Equal(["Headline a"], Titles(await GetPageAsync(host, "/web/statements?issue=health")));
        Assert.Equal(["Headline b"], Titles(await GetPageAsync(host, "/web/statements?from=2026-03-03")));
        var page = await GetPageAsync(host, "/web/statements?official=official-a");
        Assert.Contains("only statements by Ann Able", page.QuerySelector(".list-scope")!.TextContent);
        Assert.Equal("official-a", page.QuerySelector("input[type=hidden][name=official]")!.GetAttribute("value"));
        Assert.Equal("Health care", page.QuerySelector("select[name=issue] option[value=health]")!.TextContent);
        Assert.Contains("No statement matches these filters.", (await GetPageAsync(host, "/web/statements?official=nobody")).QuerySelector(".empty")!.TextContent);
    }

    [Fact]
    public async Task TheStatementListPagesByTwentyFiveAcrossDatedAndUndatedStatements()
    {
        await using var host = await StartAsync();
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase, new SeededOfficial("official-a", "Ann Able"));
        await StatementsSeed.AddAsync(host.OperationalDatabase, [.. Enumerable.Range(1, 26).Select(index =>
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
    public async Task TheExecutiveActionListShowsKindAndSignedDateWithNoAiLabelBecauseTheViewHasNoLabelColumn()
    {
        await using var host = await StartAsync();
        await SeedExecutiveActionsAsync(host, (ExecutiveId(1), "ExecutiveOrder", "Order one", "2026-01-20"), (ExecutiveId(2), "Proclamation", "Proclamation two", "2026-02-02"));

        var page = await GetPageAsync(host, "/web/executive-actions");

        Assert.Equal(["Proclamation two", "Order one"], Titles(page));
        Assert.Contains("Proclamation, Signed 2 Feb 2026", Text(Rows(page)[0]));
        Assert.Contains("Executive order, Signed 20 Jan 2026", Text(Rows(page)[1]));
        Assert.All(Rows(page), row => Assert.Null(Label(row)));
        Assert.Equal("Showing 2 executive actions on this page. Release 1.", page.QuerySelector(".list-status")!.TextContent);
        Assert.Contains("What these documents are", page.QuerySelector(".notice")!.TextContent);
        Assert.Contains("documents that the President signed and the Federal Register printed, in this release", page.QuerySelector(".list-scope")!.TextContent);
    }

    [Fact]
    public async Task TheExecutiveActionListFiltersByTypeAndDateAndPagesByTwentyFive()
    {
        await using var host = await StartAsync();
        await SeedExecutiveActionsAsync(host, [(ExecutiveId(0), "Notice", "Notice zero", "2025-12-31"),
            .. Enumerable.Range(1, 25).Select(index => (ExecutiveId(index), "ExecutiveOrder", $"Order {index:00}", $"2026-01-{index:00}"))]);

        Assert.Equal(["Notice zero"], Titles(await GetPageAsync(host, "/web/executive-actions?type=notice")));
        Assert.Equal(["Order 25"], Titles(await GetPageAsync(host, "/web/executive-actions?type=executiveOrder&from=2026-01-25")));
        var first = await GetPageAsync(host, "/web/executive-actions");
        var next = first.QuerySelector(".pager a[rel=next]")!.GetAttribute("href")!;
        var second = await GetPageAsync(host, "/web/executive-actions" + next);
        Assert.Equal(25, Rows(first).Length);
        Assert.Equal(["Notice zero"], Titles(second));
        Assert.Contains("Memorandum", (await GetPageAsync(host, "/web/executive-actions")).QuerySelectorAll("select[name=type] option").Select(option => option.TextContent));
        Assert.Contains("No executive action matches these filters.", (await GetPageAsync(host, "/web/executive-actions?type=memorandum")).QuerySelector(".empty")!.TextContent);
    }

    // ---- document changes ----

    [Fact]
    public async Task TheDocumentChangeListShowsOfficeDateAndTheAiLabelOnlyWhenTheViewHasOneAndHidesTheFiltersWhenNoneIsPublished()
    {
        await using var host = await StartAsync();
        var empty = await GetPageAsync(host, "/web/document-changes");
        Assert.Contains("No document changes are published yet.", empty.QuerySelector(".empty")!.TextContent);
        Assert.Null(empty.QuerySelector(".list-fold"));

        var created = await DocumentChanges.DocumentChangeEndpointsTests.CreateDraftAsync(host, "list", "A blue fee.\n\nStable line.", "A green fee.\n\nStable line.");
        var person = await DocumentChanges.DocumentChangeEndpointsTests.SaveAsync(host, created, "Person change", created.Citations);
        await DocumentChanges.DocumentChangeEndpointsTests.ApproveAsync(host, person);
        await DocumentChanges.DocumentChangeEndpointsTests.CommitAsync(host, 2, 1, person);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, DocumentChangeClones.SetDate(person.DraftId, "2026-03-01"));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, DocumentChangeClones.Of(person.DraftId, new string('f', 32), "Model change", "Other Office", "2026-03-05", model: true));

        var page = await GetPageAsync(host, "/web/document-changes");

        Assert.Equal(["Model change", "Person change"], Titles(page));
        Assert.Equal("Drafted by an AI model from these source versions. A person checked it before publication.", page.QuerySelectorAll(".record-row")[0].QuerySelectorAll("a > span")[1].TextContent);
        Assert.Equal(2, page.QuerySelectorAll(".record-row")[1].QuerySelectorAll("a > span").Length);
        Assert.Contains("Other Office, Found on 5 Mar 2026", Text(Rows(page)[0]));
        Assert.DoesNotContain("Changed in", Text(Rows(page)[0]));
        Assert.Contains("Example Office, Found on 1 Mar 2026, Changed in 1 place", Text(Rows(page)[1]));
        Assert.Equal("Showing 2 document changes on this page. Release 2.", page.QuerySelector(".list-status")!.TextContent);
        Assert.Equal(["Person change"], Titles(await GetPageAsync(host, "/web/document-changes?institution=Example%20Office")));
        Assert.Equal(["Model change"], Titles(await GetPageAsync(host, "/web/document-changes?from=2026-03-02")));
        Assert.Contains("No document change matches these filters.", (await GetPageAsync(host, "/web/document-changes?institution=Nobody")).QuerySelector(".empty")!.TextContent);
    }

    [Fact]
    public async Task TheDocumentChangeListPagesByTwentyFiveAcrossDatedAndUndatedChanges()
    {
        await using var host = await StartAsync();
        var created = await DocumentChanges.DocumentChangeEndpointsTests.CreateDraftAsync(host, "page", "A blue fee.\n\nStable line.", "A green fee.\n\nStable line.");
        var seed = await DocumentChanges.DocumentChangeEndpointsTests.SaveAsync(host, created, "Seed", created.Citations);
        await DocumentChanges.DocumentChangeEndpointsTests.ApproveAsync(host, seed);
        await DocumentChanges.DocumentChangeEndpointsTests.CommitAsync(host, 2, 1, seed);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, DocumentChangeClones.SetDate(seed.DraftId, "2026-03-01"));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, string.Join('\n', Enumerable.Range(1, 25).Select(index =>
            DocumentChangeClones.Of(seed.DraftId, index.ToString("00000000") + new string('c', 24), $"Change {index:00}", "Example Office", index <= 24 ? $"2026-04-{index:00}" : null, model: false))));

        var first = await GetPageAsync(host, "/web/document-changes");
        var second = await GetPageAsync(host, "/web/document-changes" + first.QuerySelector(".pager a[rel=next]")!.GetAttribute("href"));

        Assert.Equal(25, Rows(first).Length);
        Assert.Equal("Seed", Titles(first)[24]);
        Assert.Equal(["Change 25"], Titles(second));
    }
}
