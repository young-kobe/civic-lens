using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.ZipDistricts;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ZipMemberCardsEndpointsTests(PostgresCollection postgres)
{
    private const string Representative = "Representative";
    private const string Senator = "Senator";

    private async Task<ApiHost> StartAsync(bool withNames = true, bool withServerPages = false)
    {
        var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite(), options => options with
        {
            PublicBaseUrl = withServerPages ? PublicBaseUrl.Parse("https://civic-lens.test", "test") : null,
            BrowserApiUrl = withServerPages ? PublicBaseUrl.Parse("https://api.civic-lens.test", "test") : null
        });
        try
        {
            await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
            await ZipSeed.AddRecordAsync(host.OperationalDatabase, 1);
            await ZipSeed.AdmitDistrictsAsync(host.OperationalDatabase);
            if (withNames)
                await ZipSeed.AddNamesAsync(host.OperationalDatabase, 1,
                    new SeededSeat("al2", "Alice <script>alert(1)</script> & \"Two\"", Representative, "AL", 2),
                    new SeededSeat("al-sen", "Sam Senator", Senator, "AL", null, Party: "Republican"),
                    new SeededSeat("dc0", "Dee Delegate", Representative, "DC", 0));
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    [Fact]
    public async Task MembersRouteUsesThePublicSiteOriginForLinksAndPhotosWhenTheHomePreviewRunsOnTheApiHost()
    {
        await using var host = await StartAsync(withServerPages: true);
        var photo = "photos/" + new string('b', 64) + ".jpg";
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"UPDATE release_officials SET photo_path = '{photo}' WHERE release_number = 1 AND official_id = 'al2';");

        var home = new HtmlParser().ParseDocument(await host.Client.GetStringAsync("/web"));
        Assert.Equal("https://api.civic-lens.test", home.QuerySelector("[data-zip-api]")!.GetAttribute("data-zip-api"));
        var fragment = await GetDocumentAsync(host, "/v1/zip-districts/36009/members");

        Assert.Equal("https://civic-lens.test/officials/al2.html", fragment.QuerySelector(".zip-member-card h3 a")!.GetAttribute("href"));
        Assert.Equal("https://civic-lens.test/" + photo, fragment.QuerySelector(".zip-member-card img")!.GetAttribute("src"));
    }

    [Fact]
    public async Task MembersRouteRendersReleaseMetadataEscapedNamesStoredPhotoAttendanceVacancyAndTerritorySeats()
    {
        await using var host = await StartAsync();
        Assert.Null(host.Options.PublicBaseUrl);
        var photo = "photos/" + new string('b', 64) + ".jpg";
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"UPDATE release_officials SET photo_path = '{photo}' WHERE release_number = 1 AND official_id = 'al2';");
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase,
            new SeededVote('b', "2026-02-01") { Congress = 120, Casts = ["Not Voting"] });
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "UPDATE vote_member_list_entries SET person_id = 'al2' WHERE list_id = '" + new string('b', 64) + "';");

        using var response = await host.Client.GetAsync("/v1/zip-districts/36009/members");
        var html = await response.Content.ReadAsStringAsync();
        var document = new HtmlParser().ParseDocument(html);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        AssertPrivate(response);
        var fragment = document.QuerySelector("[data-zip-member-fragment]")!;
        Assert.Equal("36009", fragment.GetAttribute("data-zcta"));
        Assert.Equal("1", fragment.GetAttribute("data-release"));
        Assert.Equal("119", fragment.GetAttribute("data-congress"));
        var cards = document.QuerySelectorAll(".zip-member-card");
        Assert.Equal(4, cards.Length);
        Assert.Equal("Alice <script>alert(1)</script> & \"Two\"", cards[0].QuerySelector("h3 a")!.TextContent);
        Assert.Null(cards[0].QuerySelector("script"));
        Assert.Equal("/officials/al2.html", cards[0].QuerySelector("h3 a")!.GetAttribute("href"));
        Assert.Equal("/" + photo, cards[0].QuerySelector("img[data-portrait-image]")!.GetAttribute("src"));
        Assert.Contains("Missed means the cast is Not Voting.", cards[0].QuerySelector(".zip-member-attendance")!.TextContent);
        Assert.Contains("Representative for Alabama, district 1", cards[1].TextContent);
        Assert.Contains("No current member is in our records.", cards[1].TextContent);
        Assert.Contains("Sam Senator", cards[2].TextContent);
        Assert.Contains("Senator for Alabama", cards[2].TextContent);
        Assert.Equal(
            ["Alice <script>alert(1)</script> & \"Two\"", "Representative for Alabama, district 1", "Sam Senator", "Senator for Alabama"],
            cards.Select(card => card.QuerySelector("h3")!.TextContent.Trim()));
        var territory = await GetDocumentAsync(host, "/v1/zip-districts/20001/members");
        Assert.Contains("Delegate for District of Columbia", territory.QuerySelector(".zip-member-card")!.TextContent);
        Assert.Contains("District of Columbia has no senators.", territory.DocumentElement!.TextContent);
    }

    [Fact]
    public async Task MembersRouteFollowsTheActiveReleaseAndRollbackRestoresItsNamesAndPortrait()
    {
        await using var host = await StartAsync();
        var photo = "photos/" + new string('b', 64) + ".jpg";
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"UPDATE release_officials SET photo_path = '{photo}' WHERE release_number = 1 AND official_id = 'al2';");
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));
        await ZipSeed.AddRecordAsync(host.OperationalDatabase, 2);
        await ZipSeed.AddNamesAsync(host.OperationalDatabase, 2, new SeededSeat("al2", "Zoe New", Representative, "AL", 2));
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));

        var original = await GetDocumentAsync(host, "/v1/zip-districts/36009/members");
        Assert.Equal("1", original.QuerySelector("[data-zip-member-fragment]")!.GetAttribute("data-release"));
        Assert.Equal("Alice <script>alert(1)</script> & \"Two\"", original.QuerySelector(".zip-member-card h3 a")!.TextContent);
        Assert.Equal("/" + photo, original.QuerySelector("img[data-portrait-image]")!.GetAttribute("src"));

        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));
        var newer = await GetDocumentAsync(host, "/v1/zip-districts/36009/members");
        Assert.Equal("2", newer.QuerySelector("[data-zip-member-fragment]")!.GetAttribute("data-release"));
        Assert.Equal("Zoe New", newer.QuerySelector(".zip-member-card h3 a")!.TextContent);
        Assert.Null(newer.QuerySelector(".zip-member-card img[data-portrait-image]"));

        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        var restored = await GetDocumentAsync(host, "/v1/zip-districts/36009/members");
        Assert.Equal("1", restored.QuerySelector("[data-zip-member-fragment]")!.GetAttribute("data-release"));
        Assert.Equal("Alice <script>alert(1)</script> & \"Two\"", restored.QuerySelector(".zip-member-card h3 a")!.TextContent);
        Assert.Equal("/" + photo, restored.QuerySelector("img[data-portrait-image]")!.GetAttribute("src"));
    }

    [Fact]
    public async Task MembersRouteReturnsPrivateNoStoreProblemResponsesForInvalidMissingAndUnpublishedNames()
    {
        await using var host = await StartAsync(withNames: false);

        await AssertProblemAsync(host, "/v1/zip-districts/not-a-zip/members", HttpStatusCode.BadRequest);
        await AssertProblemAsync(host, "/v1/zip-districts/36009/members", HttpStatusCode.ServiceUnavailable);
        await ZipSeed.AddNamesAsync(host.OperationalDatabase, 1, new SeededSeat("al2", "Alice Two", Representative, "AL", 2));
        await AssertProblemAsync(host, "/v1/zip-districts/11111/members", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MembersRouteWithoutAnActiveReleaseReturnsPrivateNoStoreServiceUnavailable()
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());

        await AssertProblemAsync(host, "/v1/zip-districts/36009/members", HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task ExistingJsonRouteKeepsItsJsonContentTypeAndPublicCachePolicy()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.GetAsync("/v1/zip-districts/36009");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=3600", response.Headers.CacheControl?.ToString());
        Assert.Contains("\"release\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"zcta\":\"36009\"", json, StringComparison.Ordinal);
    }

    private static async Task<IDocument> GetDocumentAsync(ApiHost host, string path)
    {
        using var response = await host.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        AssertPrivate(response);
        return new HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync());
    }

    private static void AssertPrivate(HttpResponseMessage response)
    {
        var cache = response.Headers.CacheControl;
        Assert.NotNull(cache);
        Assert.True(cache.Private);
        Assert.True(cache.NoStore);
        Assert.False(cache.Public);
        Assert.Null(cache.MaxAge);
    }

    private static async Task AssertProblemAsync(ApiHost host, string path, HttpStatusCode status)
    {
        using var response = await host.Client.GetAsync(path);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        AssertPrivate(response);
    }
}
