using System.Net;
using AngleSharp.Html.Parser;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Officials;
using CivicLens.Tests.PublicApi.Records;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Pages;

/// <summary>The route map of the cutover: every page path that <see cref="PublicationProtocol"/> names answers from PublicApi at that public path, with no prefix.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicRouteMapTests(PostgresCollection postgres)
{
    private const string Root = "https://civic-lens.test/";
    private const string OfficialId = "p0";
    private const string IssueId = "health";
    private static readonly SeededVote House = new('a', "2026-03-02") { Casts = ["Yea", "Nay"], PersonIds = [OfficialId, "p1"], Issues = [IssueId] };

    private static SeededTerm Term(string state, int district) => new("Representative", "Representative", "2025-01-03") { State = state, District = district };

    private async Task<ApiHost> StartAsync()
    {
        var host = await ApiHost.StartAsync(postgres, options => options with { PublicBaseUrl = PublicBaseUrl.Parse(Root, "test") });
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, House);
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase, new SeededOfficial(OfficialId, "Ann Able", [Term("CA", 12)]), new SeededOfficial("p1", "Ben Baker", [Term("TX", 1)]));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            UPDATE vote_member_list_entries SET party = CASE person_id WHEN '{OfficialId}' THEN 'Democrat' ELSE 'Republican' END WHERE list_id = '{House.RecordId}';
            INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (1, '{IssueId}', 'Health care', 'Defined');
            """);
        await CorrectionHistorySeed.BindEmptyAsync(host.OperationalDatabase);
        return host;
    }

    /// <summary>Every page path that the protocol names and the release can fill. Search and report pages are included, though the sitemap leaves them out.</summary>
    private static IEnumerable<string> PagePaths() =>
    [
        string.Empty,
        PublicationProtocol.HomePath,
        PublicationProtocol.ComparePagePath,
        PublicationProtocol.OfficialsPagePath,
        PublicationProtocol.BoardsPagePath,
        .. OfficialBoard.All.Select(board => board.PagePath),
        PublicationProtocol.FacetsPath,
        .. new[] { PublishedRecordKind.Vote, PublishedRecordKind.Action, PublishedRecordKind.Statement, PublishedRecordKind.ExecutiveAction }.Select(PublicationProtocol.ListPagePath),
        PublicationProtocol.SearchPagePath,
        PublicationProtocol.VoteKindsPagePath,
        PublicationProtocol.GlossaryPagePath,
        PublicationProtocol.ReportPagePath,
        PublicationProtocol.MethodPagePath,
        PublicationProtocol.AboutPagePath,
        PublicationProtocol.BriefsPagePath(1),
        PublicationProtocol.OfficialPagePath(OfficialId),
        PublicationProtocol.OfficialRecordPagePath(OfficialId),
        PublicationProtocol.IssuePagePath(IssueId, 1),
        PublicationProtocol.RecordPagePath(House.RecordId),
        PublicationProtocol.VoteMapPagePath(House.RecordId)
    ];

    [Fact]
    public async Task EveryProtocolPagePathAnswersAtItsPublicPathAndNoRenderedLinkOrFormActionUsesTheOldWebPrefix()
    {
        await using var host = await StartAsync();

        foreach (var path in PagePaths())
        {
            using var response = await host.Client.GetAsync("/" + path);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"/{path} gave {(int)response.StatusCode}: {body}");
            var page = await new HtmlParser().ParseDocumentAsync(body);
            Assert.NotEqual("Page not found", page.QuerySelector("h1")?.TextContent);
            var addresses = page.QuerySelectorAll("[href], [action], [src]")
                .SelectMany(element => new[] { element.GetAttribute("href"), element.GetAttribute("action"), element.GetAttribute("src") }).OfType<string>().ToArray();
            Assert.NotEmpty(addresses);
            Assert.All(addresses, address => Assert.False(address.StartsWith("/web", StringComparison.Ordinal) || address.StartsWith(Root + "web", StringComparison.Ordinal),
                $"/{path} links {address}"));
        }
    }

    [Theory]
    [InlineData(PublicationProtocol.SitemapPath)]
    [InlineData(PublicationProtocol.RobotsPath)]
    public async Task TheSitemapAndRobotsFileAnswerAtTheirPublicPaths(string path)
    {
        await using var host = await StartAsync();

        using var response = await host.Client.GetAsync("/" + path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("web")]
    [InlineData("web/votes")]
    [InlineData("web/officials")]
    public async Task AnOldWebPrefixPathAnswersTheNotFoundPage(string path)
    {
        await using var host = await StartAsync();

        using var response = await host.Client.GetAsync("/" + path);
        var page = await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Page not found", page.QuerySelector("h1")!.TextContent);
    }

    [Fact]
    public async Task AnUnknownApiPathAnswersAnEmpty404AndNotThePage()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.GetAsync("/v1/no-such-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }
}
