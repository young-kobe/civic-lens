using System.Net;
using System.Net.Http.Headers;
using System.Xml.Linq;
using CivicLens.PublicApi.Pages;
using CivicLens.PublicApi.Sitemap;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Home;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Sitemap;

/// <summary>The gated sitemap and robots file that the server builds from the public views in place of the release files.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class SitemapEndpointsTests(PostgresCollection postgres)
{
    private const string Root = "https://civic-lens.test/";
    private static readonly XNamespace Namespace = "http://www.sitemaps.org/schemas/sitemap/0.9";
    private static readonly string Vote = new('a', 64);
    private static readonly string Statement = new('b', 64);
    private static readonly string Relationship = new('c', 64);
    private static readonly string Withdrawn = new('d', 64);

    private async Task<ApiHost> StartAsync()
    {
        var host = await ApiHost.StartAsync(postgres, options => options with { PublicBaseUrl = PublicBaseUrl.Parse(Root, "test") });
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 4, new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero));
        return host;
    }

    private static Task<HttpResponseMessage> GetAsync(ApiHost host, string path, string? etag = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{ServerPageEndpoints.Prefix}/{path}");
        if (etag is not null) request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(etag));
        return host.Client.SendAsync(request);
    }

    private static async Task<XElement> XmlAsync(ApiHost host, string path)
    {
        using var response = await GetAsync(host, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return XElement.Parse(await response.Content.ReadAsStringAsync());
    }

    private static string[] Locations(XElement root, string entry) =>
        [.. root.Elements(Namespace + entry).Select(element => element.Element(Namespace + "loc")!.Value)];

    private static Task SeedStatementsAsync(ApiHost host, int count) => ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
        SET session_replication_role = replica;
        INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
        SELECT 'statement', lpad(to_hex(n), 64, '0'), 1, 1, NULL FROM generate_series(1, {count}) n;
        """);

    [Fact]
    public async Task TheSitemapListsEveryPublicPageOfTheReleaseAtItsPublicPathAndLeavesOutSearchReportAndWithdrawnRecords()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            SET session_replication_role = replica;
            INSERT INTO release_officials (release_number, official_id, name) VALUES (4, 'b0', 'Bea Bell'), (4, 'a0', 'Al Ash'), (3, 'gone', 'Old Name');
            INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (4, 'tax', 'Taxes', 'd'), (3, 'old', 'Old', 'd');
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
            VALUES ('vote', '{Vote}', 1, 1, NULL), ('statement', '{Statement}', 1, 2, NULL), ('relationship', '{Relationship}', 2, 1, NULL),
                   ('statement', '{Withdrawn}', 1, 1, 3);
            """);
        var briefs = Enumerable.Range(0, PublicationProtocol.PageSize + 1)
            .Select(index => new SeededBrief((char)(0x100 + index), new DateOnly(2026, 1, 5).AddDays(index * 7).ToString("yyyy-MM-dd")) { RecordId = (index + 1).ToString("x64") }).ToArray();
        await HomeSeed.AddBriefsAsync(host.OperationalDatabase, briefs);

        var urls = Locations(await XmlAsync(host, PublicationProtocol.SitemapPath), "url");

        string[] pages =
        [
            "", "officials.html", "boards.html", "board-house-democrat.html", "board-house-republican.html", "board-senate-democrat.html",
            "board-senate-republican.html", "issues.html", "votes.html", "bills.html", "statements.html", "executive-actions.html", "compare.html",
            "vote-kinds.html", "glossary.html", "method.html", "about.html", "briefs.html", "briefs-2.html",
            "officials/a0.html", "officials/b0.html", "officials/a0/record.html", "officials/b0/record.html", "issues/tax.html",
            .. briefs.Select(brief => $"records/{brief.RecordId}.html").Order(StringComparer.Ordinal),
            $"records/{Vote}.html", $"records/{Statement}.html", $"records/{Relationship}.html", $"records/{Vote}/map.html"
        ];
        Assert.Equal(pages.Select(page => Root + page), urls);
        Assert.DoesNotContain(urls, url => url.Contains(Withdrawn, StringComparison.Ordinal) || url.Contains("/web/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheSitemapIsXmlWithTheReleaseValidatorsAndAMatchingRequestGets304()
    {
        await using var host = await StartAsync();

        using var response = await GetAsync(host, PublicationProtocol.SitemapPath);
        using var again = await GetAsync(host, PublicationProtocol.SitemapPath, response.Headers.ETag!.ToString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/xml; charset=utf-8", response.Content.Headers.ContentType!.ToString());
        Assert.Equal(PageCache.CacheControl, response.Headers.CacheControl!.ToString());
        Assert.StartsWith("W/\"r4-", response.Headers.ETag.ToString());
        Assert.False(response.Headers.Contains("X-Robots-Tag"));
        Assert.Equal(HttpStatusCode.NotModified, again.StatusCode);
        Assert.Equal(PageCache.CacheControl, again.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task TheRobotsFileAllowsEveryCrawlerAndNamesThePublicSitemapAsPlainText()
    {
        await using var host = await StartAsync();

        using var response = await GetAsync(host, PublicationProtocol.RobotsPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"User-agent: *\nAllow: /\nSitemap: {Root}sitemap.xml\n", await response.Content.ReadAsStringAsync());
        Assert.Equal("text/plain; charset=utf-8", response.Content.Headers.ContentType!.ToString());
        Assert.Equal(PageCache.CacheControl, response.Headers.CacheControl!.ToString());
        Assert.False(response.Headers.Contains("X-Robots-Tag"));
    }

    [Fact]
    public async Task OverTheProtocolLimitTheSitemapIsAnIndexAndEachFileHoldsAtMostTheLimit()
    {
        await using var host = await StartAsync();
        await SeedStatementsAsync(host, SitemapReader.MaximumUrls);

        var index = Locations(await XmlAsync(host, PublicationProtocol.SitemapPath), "sitemap");
        var first = Locations(await XmlAsync(host, PublicationProtocol.SitemapPagePath(1)), "url");
        var second = Locations(await XmlAsync(host, PublicationProtocol.SitemapPagePath(2)), "url");
        using var third = await GetAsync(host, PublicationProtocol.SitemapPagePath(3));

        Assert.Equal([Root + "sitemap-1.xml", Root + "sitemap-2.xml"], index);
        Assert.Equal(SitemapReader.MaximumUrls, first.Length);
        Assert.Equal(Root, first[0]);
        Assert.Equal(SitemapReader.MaximumUrls + 18, first.Length + second.Length);
        Assert.Equal(first.Length + second.Length, first.Concat(second).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Root + PublicationProtocol.RecordPagePath(SitemapReader.MaximumUrls.ToString("x64")), second[^1]);
        Assert.Equal(HttpStatusCode.NotFound, third.StatusCode);
    }

    [Fact]
    public async Task AtTheProtocolLimitTheSitemapIsOneFileAndNoNumberedFileExists()
    {
        await using var host = await StartAsync();
        await SeedStatementsAsync(host, SitemapReader.MaximumUrls - 18);

        var urls = Locations(await XmlAsync(host, PublicationProtocol.SitemapPath), "url");
        using var numbered = await GetAsync(host, PublicationProtocol.SitemapPagePath(1));

        Assert.Equal(SitemapReader.MaximumUrls, urls.Length);
        Assert.Equal(HttpStatusCode.NotFound, numbered.StatusCode);
    }

    [Fact]
    public async Task WithoutAnActiveReleaseTheSitemapAnswers503()
    {
        await using var host = await ApiHost.StartAsync(postgres, options => options with { PublicBaseUrl = PublicBaseUrl.Parse(Root, "test") });

        using var response = await GetAsync(host, PublicationProtocol.SitemapPath);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
