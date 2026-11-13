using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicApi;
using CivicLens.PublicApi.Pages;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Publication;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Officials;
using Microsoft.Extensions.Logging;

namespace CivicLens.Tests.PublicApi.Votes;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class VoteMapPageEndpointsTests(PostgresCollection postgres)
{
    private static readonly PublicBaseUrl BaseUrl = PublicBaseUrl.Parse("https://civic-lens.test", "test")!;
    private static readonly SeededVote House = new('a', "2026-03-02") { Casts = ["Yea", "Nay", "Nay", "Not Voting"], PersonIds = ["p0", "p1", "p2", "p3"] };
    private static readonly string MapPath = $"{ServerPageEndpoints.Prefix}/{PublicationProtocol.VoteMapPagePath(House.RecordId)}";

    private static SeededTerm Term(string state, int district) => new("Representative", "Representative", "2025-01-03") { State = state, District = district };

    private async Task<ApiHost> StartAsync(bool withSeats = true, ILoggerProvider? logs = null, bool everyMemberNamed = true)
    {
        var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite(), options => options with { PublicBaseUrl = BaseUrl }, logProvider: logs);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, House);
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase,
            [.. new[] { new SeededOfficial("p0", "Ann Able", withSeats ? [Term("CA", 12)] : []), new SeededOfficial("p1", "Ben Baker", withSeats ? [Term("TX", 1)] : []),
                new SeededOfficial("p2", "Cy Cole", withSeats ? [Term("TX", 2)] : []), new SeededOfficial("p3", "Di Dole", withSeats ? [Term("AK", 0)] : []) }.Take(everyMemberNamed ? 4 : 3)]);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            UPDATE vote_member_list_entries SET party = CASE person_id WHEN 'p0' THEN 'Democrat' WHEN 'p1' THEN 'Democrat' ELSE 'Republican' END WHERE list_id = '{House.RecordId}';
            """);
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
    public async Task TheMapRouteServesTheMapWithBothLegendsTheColorButtonsAndTheSeatTable()
    {
        await using var host = await StartAsync();

        var page = await GetPageAsync(host, MapPath);

        Assert.Equal("How the House voted: roll call 1, H.R. 1", page.QuerySelector("h1")!.TextContent);
        Assert.Equal(["Home", "Votes", "House roll call 1"], page.QuerySelectorAll(".public-breadcrumbs a").Select(link => link.TextContent));
        Assert.Equal("Map", page.QuerySelector(".public-breadcrumbs [aria-current=page]")!.TextContent);
        Assert.Equal([VoteMapWords.ByVote, VoteMapWords.ByParty], page.QuerySelectorAll("[data-vote-map-colors] button").Select(button => button.TextContent));
        Assert.Equal(["1 Yes", "2 No", "1 Not voting", "431 No member in the record"], page.QuerySelectorAll(".vote-map-legend-vote li").Select(item => item.TextContent.Trim()));
        Assert.Equal(["Democrats: 1 Yes, 1 No", "Republicans: 1 No, 1 Not voting", "431 No member in the record"], page.QuerySelectorAll(".vote-map-legend-party li").Select(item => item.TextContent.Trim()));
        Assert.Equal(435, page.QuerySelectorAll(".vote-map-shapes .vote-map-seat").Length);
        Assert.Equal(435, page.QuerySelectorAll(".vote-map details.vote-fold tbody tr").Length);
        Assert.Equal(Root(PublicationProtocol.RecordPagePath(House.RecordId)), page.QuerySelector(".page-header a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task AVoteWhoseMembersHaveNoSeatShowsTheEmptyStateNotAMap()
    {
        await using var host = await StartAsync(withSeats: false);

        var page = await GetPageAsync(host, MapPath);

        Assert.Null(page.QuerySelector(".vote-map"));
        Assert.Equal(VoteMapWords.NoMapTitle, page.QuerySelector(".empty h3")!.TextContent);
    }

    [Theory]
    [InlineData("cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc")]
    [InlineData("not-a-record")]
    public async Task AnUnknownOrMalformedRecordHasNoMapPage(string recordId)
    {
        await using var host = await StartAsync();

        await GetPageAsync(host, $"{ServerPageEndpoints.Prefix}/{PublicationProtocol.VoteMapPagePath(recordId)}", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheMapRouteAnswers304ForTheETagOfItsRelease()
    {
        await using var host = await StartAsync();
        using var first = await host.Client.GetAsync(MapPath);
        using var request = new HttpRequestMessage(HttpMethod.Get, MapPath);
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);

        using var second = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task TheHomeRouteDrawsTheFeaturedMapFromTheSameReleaseAndLinksToTheMapPage()
    {
        await using var host = await StartAsync();

        var page = await GetPageAsync(host, ServerPageEndpoints.Prefix);

        Assert.Equal(1, page.QuerySelectorAll(".home-featured .vote-map[data-compact]").Length);
        Assert.Equal(MapPath, page.QuerySelector(".home-featured-map-link")!.GetAttribute("href"));
        using var map = await host.Client.GetAsync(page.QuerySelector(".home-featured-map-link")!.GetAttribute("href"));
        Assert.Equal(HttpStatusCode.OK, map.StatusCode);
        Assert.Contains("1 Yes, 2 No, 1 Not voting, 431 No member in the record", page.QuerySelector(".home-featured .vote-map-shapes")!.GetAttribute("aria-label"));
        Assert.Null(page.QuerySelector(".home-breakers"));
    }

    [Fact]
    public async Task TheHomeRouteWithoutSeatsKeepsTheFeaturedVoteAndOmitsTheMap()
    {
        await using var host = await StartAsync(withSeats: false);

        var page = await GetPageAsync(host, ServerPageEndpoints.Prefix);

        Assert.NotNull(page.QuerySelector(".home-featured h2"));
        Assert.Null(page.QuerySelector(".home-featured .vote-map"));
        Assert.Null(page.QuerySelector(".home-featured-map-link"));
    }

    [Fact]
    public async Task TheHomeRouteLogsAWarningWithTheRecordIdWhenItLeavesOutTheFeaturedMap()
    {
        var logs = new CapturedLogs();
        await using var host = await StartAsync(logs: logs, everyMemberNamed: false);

        var page = await GetPageAsync(host, ServerPageEndpoints.Prefix);

        Assert.NotNull(page.QuerySelector(".home-featured h2"));
        Assert.Null(page.QuerySelector(".home-featured .vote-map"));
        Assert.Contains(logs.Lines, line => line.Contains(House.RecordId, StringComparison.Ordinal) && line.Contains("has no official name", StringComparison.Ordinal));
    }

    private static string Root(string path) => "https://civic-lens.test/" + path;
}
