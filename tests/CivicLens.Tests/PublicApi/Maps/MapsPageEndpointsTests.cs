using System.Net;
using System.Net.Http.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicApi;
using CivicLens.PublicApi.Maps;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Publication;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Officials;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Maps;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class MapsPageEndpointsTests(PostgresCollection postgres)
{
    private static readonly PublicBaseUrl BaseUrl = PublicBaseUrl.Parse("https://civic-lens.test", "test")!;
    private static readonly PublicBaseUrl BrowserApiUrl = PublicBaseUrl.Parse("https://api.civic-lens.test", PublicApiOptions.BrowserApiUrlVariable)!;
    private static readonly string MapPath = $"/{PublicationProtocol.MapsPagePath}";

    [Fact]
    public async Task TheDefaultMapShowsAttendanceAndRevalidatesTheSamePageWithItsReleaseTag()
    {
        await using var host = await StartAsync(withVotes: true);
        using var response = await host.Client.GetAsync(MapPath);
        var page = await ReadPageAsync(response);

        Assert.Equal(MapsWords.Heading, page.QuerySelector("h1")!.TextContent);
        var form = page.QuerySelector("form.maps-controls")!;
        Assert.Equal("get", form.GetAttribute("method"));
        Assert.Equal(BaseUrl.Join(PublicationProtocol.MapsPagePath), form.GetAttribute("action"));
        Assert.Equal("119", Selected(page, "congress"));
        Assert.Equal("house", Selected(page, "chamber"));
        Assert.Equal("attendance", Selected(page, "layer"));
        var memberUrl = BaseUrl.Join(PublicationProtocol.OfficialPagePath("member"));
        var row = page.QuerySelector("#maps-table tbody tr")!;
        Assert.Equal(memberUrl, row.QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains("1 of 2", row.TextContent);
        var dataLink = page.QuerySelector("#maps-table .table-wrap > p a")!;
        Assert.Equal("Get this data", dataLink.TextContent);
        Assert.Equal(BrowserApiUrl.Join("v1/maps?congress=119&chamber=house&layer=attendance"), dataLink.GetAttribute("href"));

        Assert.True(response.Headers.CacheControl is { Public: true });
        Assert.NotNull(response.Headers.ETag);
        using var request = new HttpRequestMessage(HttpMethod.Get, MapPath);
        request.Headers.IfNoneMatch.Add(response.Headers.ETag);
        using var repeated = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotModified, repeated.StatusCode);
    }

    [Fact]
    public async Task ChangingLayersIgnoresStaleValuesAndUsesTheSavedDateAndRequestedMeasure()
    {
        await using var host = await StartAsync(withVotes: true);

        using var termResponse = await host.Client.GetAsync(MapPath + "?layer=termEnds&issue=old-issue&measure=old-measure");
        var term = await ReadPageAsync(termResponse);
        Assert.Equal("termEnds", Selected(term, "layer"));
        Assert.Equal("2027-01-03", Selected(term, "end"));
        Assert.NotNull(term.QuerySelector(".vote-map-tiles [data-mark=term-selected]"));
        Assert.NotNull(term.QuerySelector(".vote-map-tiles [data-mark=term-other]"));

        using var endResponse = await host.Client.GetAsync(MapPath + "?layer=termEnds&end=2029-01-03");
        var end = await ReadPageAsync(endResponse);
        Assert.Equal("2029-01-03", Selected(end, "end"));
        var selectedTerm = end.QuerySelector(".vote-map-tiles [data-mark=term-selected]")!.ParentElement!;
        Assert.Equal(BaseUrl.Join(PublicationProtocol.OfficialPagePath("other")), selectedTerm.GetAttribute("href"));

        using var issueResponse = await host.Client.GetAsync(MapPath + "?layer=issueActivity&issue=health&measure=votes&end=old-end");
        var issue = await ReadPageAsync(issueResponse);
        Assert.Equal("health", Selected(issue, "issue"));
        Assert.Equal("votes", Selected(issue, "measure"));
        Assert.Contains("1 of 1", issue.QuerySelector("#maps-table tbody tr")!.TextContent);
        Assert.NotNull(issue.QuerySelector(".vote-map-tiles [data-mark=measure]"));
        Assert.Equal(BrowserApiUrl.Join("v1/maps?congress=119&chamber=house&layer=issueActivity&issue=health"),
            issue.QuerySelector("#maps-table .table-wrap > p a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task RefusedFiltersShowTheSubmittedFormAndChoicesWithoutCacheOrResults()
    {
        await using var host = await StartAsync();
        using var initial = await host.Client.GetAsync(MapPath);
        Assert.NotNull(initial.Headers.ETag);
        foreach (var (query, name, value) in new (string, string, string?)[]
        {
            ("?congress=120", "congress", "120"),
            ("?chamber=%3Cunknown%3E", "chamber", "<unknown>"),
            ("?layer=attendance&layer=termEnds", "layer", "attendance"),
            ("?layer=issueActivity", "issue", null),
            ("?layer=issueActivity&issue=missing", "issue", "missing"),
            ("?layer=issueActivity&issue=health&measure=unknown", "measure", "unknown"),
            ("?layer=attendance&measure=bills&measure=votes", "measure", "bills"),
            ("?layer=termEnds&end=2027-1-3", "end", "2027-1-3"),
            ("?layer=termEnds&end=2028-01-03", "end", "2028-01-03")
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, MapPath + query);
            request.Headers.IfNoneMatch.Add(initial.Headers.ETag);
            using var response = await host.Client.SendAsync(request);
            var page = await ReadPageAsync(response, HttpStatusCode.BadRequest);

            Assert.True(response.Headers.CacheControl is { NoStore: true });
            Assert.Null(response.Headers.ETag);
            Assert.Single(page.QuerySelectorAll("form.maps-controls [aria-invalid=true]"));
            Assert.NotNull(page.QuerySelector($"form.maps-controls select[name={name}][aria-invalid=true]"));
            Assert.Null(page.QuerySelector($"form.maps-controls [hidden] select[name={name}][aria-invalid=true]"));
            Assert.Equal(value ?? string.Empty, Selected(page, name) ?? string.Empty);
            Assert.NotNull(page.QuerySelector("select[name=issue] option[value=health]"));
            if (name == "end") Assert.NotNull(page.QuerySelector("select[name=end] option[value='2027-01-03']"));
            Assert.Null(page.QuerySelector(".maps-explorer"));
            Assert.Null(page.QuerySelector("#maps-table"));
        }
    }

    [Fact]
    public async Task AReleaseWithOnlySeatTermsKeepsNamedMembersAndMissingSeatsSeparateFromNoTerms()
    {
        await using var host = await StartAsync();

        using var rosterResponse = await host.Client.GetAsync(MapPath + "?chamber=senate");
        var roster = await ReadPageAsync(rosterResponse);
        var row = Assert.Single(roster.QuerySelectorAll("#maps-table tbody tr"));
        Assert.Contains("Unplaced Senator", row.TextContent);
        Assert.Contains(MapsWords.UnknownSeat, row.TextContent);
        Assert.Contains(MapsWords.Unknown, row.TextContent);
        Assert.Null(roster.QuerySelector(".vote-map-frame a"));

        using var noTermsResponse = await host.Client.GetAsync(MapPath + "?congress=118");
        var noTerms = await ReadPageAsync(noTermsResponse);
        Assert.Contains(MapsWords.Empty, noTerms.Body!.TextContent);
        Assert.Equal("118", Selected(noTerms, "congress"));
        Assert.Null(noTerms.QuerySelector(".maps-explorer"));
        Assert.Null(noTerms.QuerySelector("#maps-table"));
    }

    [Fact]
    public async Task ThreeClaimedSenateClassesKeepAllNamesWithoutDrawingAThirdSeat()
    {
        await using var host = await StartAsync();
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase,
            new("first", "First Claim", new SeededTerm("Senator", "Senator", "2025-01-03") { State = "MA" }),
            new("second", "Second Claim", new SeededTerm("Senator", "Senator", "2025-01-03") { State = "MA" }),
            new("third", "Third Claim", new SeededTerm("Senator", "Senator", "2025-01-03") { State = "MA" }));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, """
            UPDATE release_official_terms SET senate_class = CASE official_id WHEN 'first' THEN 1 WHEN 'second' THEN 2 ELSE 3 END
             WHERE official_id IN ('first', 'second', 'third');
            """);
        var answer = await host.Client.GetFromJsonAsync<MapAnswer>("/v1/maps?chamber=senate&layer=termEnds", PublicationProtocol.JsonOptions);
        Assert.NotNull(answer);
        Assert.Equal(4, answer.Members.Length);
        Assert.Empty(answer.Seats);

        using var response = await host.Client.GetAsync(MapPath + "?chamber=senate&layer=termEnds");
        var page = await ReadPageAsync(response);
        var rows = page.QuerySelectorAll("#maps-table tbody tr");
        Assert.Equal(4, rows.Length);
        Assert.All(rows, row => Assert.Contains(MapsWords.UnknownSeat, row.TextContent));
        Assert.Null(page.QuerySelector(".vote-map-frame a"));
    }

    [Fact]
    public async Task NoActiveReleaseShowsTheHtmlServiceUnavailablePage()
    {
        await using var host = await ApiHost.StartAsync(postgres, options => options with { PublicBaseUrl = BaseUrl });

        using var response = await host.Client.GetAsync(MapPath);
        var page = await ReadPageAsync(response, HttpStatusCode.ServiceUnavailable);

        Assert.Equal(PublishedServerErrorPage.Title, page.QuerySelector("h1")!.TextContent);
        Assert.True(response.Headers.CacheControl is { NoStore: true });
        Assert.Null(response.Headers.ETag);
    }

    private async Task<ApiHost> StartAsync(bool withVotes = false)
    {
        var host = await ApiHost.StartAsync(postgres, options => options with { PublicBaseUrl = BaseUrl, BrowserApiUrl = BrowserApiUrl });
        try
        {
            var database = host.OperationalDatabase;
            await ViewsSeed.ActivateReleaseAsync(database, 1, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), recordCount: withVotes ? 2 : 0);
            await OfficialsSeed.AddOfficialsAsync(database,
                new("member", "First Member", new SeededTerm("Representative", "Representative", "2025-01-03") { State = "AL", District = 1, EndsOn = "2027-01-03" }),
                new("other", "Other Member", new SeededTerm("Representative", "Representative", "2025-01-03") { State = "AL", District = 2, EndsOn = "2029-01-03" }),
                new("senator", "Unplaced Senator", new SeededTerm("Senator", "Senator", "2025-01-03") { State = "WA" }));
            await ViewsSeed.ExecuteAsync(database, "INSERT INTO release_issues (release_number, issue_id, name) VALUES (1, 'health', 'Health');");
            if (withVotes)
                await ViewsSeed.AddVotesAsync(database,
                    new SeededVote('a', "2026-02-03") { PersonIds = ["member"], Casts = ["Yea"], Issues = ["health"] },
                    new SeededVote('b', "2026-03-03") { RollCall = 2, PersonIds = ["member"], Casts = ["Not Voting"] });
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    private static string? Selected(IDocument page, string name) => page.QuerySelector($"select[name={name}] option[selected]")?.GetAttribute("value");

    private static async Task<IDocument> ReadPageAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{response.RequestMessage?.RequestUri} gave {(int)response.StatusCode}: {body}");
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        return await new HtmlParser().ParseDocumentAsync(body);
    }
}
