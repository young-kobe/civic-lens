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
        Assert.Equal("https://api.civic-lens.test/web/compare", home.QuerySelectorAll(".home-browse a").Single(link => link.TextContent == "Compare your members").GetAttribute("href"));
        var fragment = await GetDocumentAsync(host, "/v1/zip-districts/36009/members");

        Assert.Equal("", fragment.QuerySelector("[data-zip-member-fragment]")!.GetAttribute("data-view"));
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
        await AddSplitPartyLineAsync(host.OperationalDatabase, 'c', 1, "al2", "Democrat", 120, "2027-02-01");
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
        var partyLine = cards[0].QuerySelector(".zip-member-party-line")!;
        Assert.Contains("With most House Democrats on 1 of 1 split vote in Congress 120.", partyLine.TextContent);
        Assert.Contains("substantive split roll calls of the House in Congress 120", partyLine.QuerySelector(".term-popover")!.TextContent);
        Assert.Null(cards[1].QuerySelector(".zip-member-party-line"));
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
    public async Task MembersRouteFiltersSharedVotesAndKeepsAllIssueOptionsIncludingZeroVoteIssues()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (1, 'health', 'Health <script>& policy', 'Health'), (1, 'zero', 'Zero votes', 'No votes');");
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase,
            new SeededVote('a', "2026-05-01") { RecordId = 1.ToString("x64"), Casts = ["Yea"], PersonIds = ["al2"], Issues = ["health"] },
            new SeededVote('b', "2026-05-02") { RecordId = 2.ToString("x64"), Casts = ["Nay"], PersonIds = ["al2"], Issues = ["other"] });

        var all = await GetDocumentAsync(host, "/v1/zip-districts/36009/members");
        var allRoot = all.QuerySelector("[data-zip-member-fragment]")!;
        Assert.Equal("", allRoot.GetAttribute("data-issue"));
        Assert.Equal(4, all.QuerySelectorAll(".zip-member-card").Length);
        Assert.Equal(2, all.QuerySelectorAll(".shared-vote-row").Length);
        var options = all.QuerySelectorAll("[data-member-issue]");
        Assert.Equal(["", "health", "zero"], options.Select(option => option.GetAttribute("data-member-issue")));
        Assert.Equal("Health <script>& policy", options[1].TextContent);
        Assert.Null(options[1].QuerySelector("script"));
        Assert.Equal("true", options[0].GetAttribute("aria-pressed"));

        var filtered = await GetDocumentAsync(host, "/v1/zip-districts/36009/members?issue=health");
        Assert.Equal("health", filtered.QuerySelector("[data-zip-member-fragment]")!.GetAttribute("data-issue"));
        Assert.Equal(4, filtered.QuerySelectorAll(".zip-member-card").Length);
        Assert.Single(filtered.QuerySelectorAll(".shared-vote-row"));
        Assert.Equal("true", Assert.Single(filtered.QuerySelectorAll("[data-member-issue=health]")).GetAttribute("aria-pressed"));

        var noMatch = await GetDocumentAsync(host, "/v1/zip-districts/36009/members?issue=unknown");
        Assert.Equal("unknown", noMatch.QuerySelector("[data-zip-member-fragment]")!.GetAttribute("data-issue"));
        Assert.Empty(noMatch.QuerySelectorAll(".shared-vote-row"));
        Assert.Equal("No records match.", noMatch.QuerySelector("[data-member-votes-empty]")!.TextContent.Trim());
        Assert.Equal("true", Assert.Single(noMatch.QuerySelectorAll("[data-member-issue=unknown]")).GetAttribute("aria-pressed"));
    }

    [Fact]
    public async Task MembersRouteFollowsTheActiveReleaseAndRollbackRestoresItsNamesAndPortrait()
    {
        await using var host = await StartAsync();
        await AddSplitPartyLineAsync(host.OperationalDatabase, 'c', 1, "al2", "Democrat", 119, "2026-02-01");
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (1, 'health', 'Health One', 'Health');");
        var photo = "photos/" + new string('b', 64) + ".jpg";
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"UPDATE release_officials SET photo_path = '{photo}' WHERE release_number = 1 AND official_id = 'al2';");
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2027, 6, 9, 0, 0, 0, TimeSpan.Zero));
        await ZipSeed.AddRecordAsync(host.OperationalDatabase, 2);
        await ZipSeed.AddNamesAsync(host.OperationalDatabase, 2, new SeededSeat("al2", "Zoe New", Representative, "AL", 2));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (2, 'tax', 'Tax Two', 'Tax');");
        await AddSplitPartyLineAsync(host.OperationalDatabase, 'd', 2, "al2", "Republican", 120, "2027-02-02");
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));

        var original = await GetDocumentAsync(host, "/v1/zip-districts/36009/members");
        Assert.Equal("1", original.QuerySelector("[data-zip-member-fragment]")!.GetAttribute("data-release"));
        Assert.Equal("Alice <script>alert(1)</script> & \"Two\"", original.QuerySelector(".zip-member-card h3 a")!.TextContent);
        Assert.Equal("/" + photo, original.QuerySelector("img[data-portrait-image]")!.GetAttribute("src"));
        Assert.Contains("House Democrats", original.QuerySelector(".zip-member-party-line")!.TextContent);
        Assert.Equal(["health"], original.QuerySelectorAll("[data-member-issue]").Select(option => option.GetAttribute("data-member-issue")).Where(issue => issue != ""));

        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2027, 6, 9, 0, 0, 0, TimeSpan.Zero));
        var newer = await GetDocumentAsync(host, "/v1/zip-districts/36009/members");
        Assert.Equal("2", newer.QuerySelector("[data-zip-member-fragment]")!.GetAttribute("data-release"));
        Assert.Equal("Zoe New", newer.QuerySelector(".zip-member-card h3 a")!.TextContent);
        Assert.Null(newer.QuerySelector(".zip-member-card img[data-portrait-image]"));
        Assert.Contains("With most House Republicans on 0 of 1 split vote in Congress 120.", newer.QuerySelector(".zip-member-party-line")!.TextContent);
        Assert.Equal(["tax"], newer.QuerySelectorAll("[data-member-issue]").Select(option => option.GetAttribute("data-member-issue")).Where(issue => issue != ""));

        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        var restored = await GetDocumentAsync(host, "/v1/zip-districts/36009/members");
        Assert.Equal("1", restored.QuerySelector("[data-zip-member-fragment]")!.GetAttribute("data-release"));
        Assert.Equal("Alice <script>alert(1)</script> & \"Two\"", restored.QuerySelector(".zip-member-card h3 a")!.TextContent);
        Assert.Equal("/" + photo, restored.QuerySelector("img[data-portrait-image]")!.GetAttribute("src"));
        Assert.Contains("House Democrats", restored.QuerySelector(".zip-member-party-line")!.TextContent);
        Assert.Equal(["health"], restored.QuerySelectorAll("[data-member-issue]").Select(option => option.GetAttribute("data-member-issue")).Where(issue => issue != ""));
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
    public async Task MembersRouteRejectsRepeatedAndOversizedIssueFilters()
    {
        await using var host = await StartAsync();

        await AssertProblemAsync(host, "/v1/zip-districts/36009/members?issue=health&issue=tax", HttpStatusCode.BadRequest);
        await AssertProblemAsync(host, "/v1/zip-districts/36009/members?issue=" + new string('x', PublishedRecordEntry.MaximumFilterValueLength + 1), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CompareLayoutKeepsEveryCurrentHouseAndSenateSeatInThePrivateSnapshot()
    {
        await using var host = await StartAsync();

        var document = await GetDocumentAsync(host, "/v1/zip-districts/36009/members?view=compare");
        var fragment = document.QuerySelector("[data-zip-member-fragment]")!;

        Assert.Equal("compare", fragment.GetAttribute("data-view"));
        Assert.Equal(4, document.QuerySelectorAll(".zip-member-card").Length);
        Assert.Equal(["house", "senate"], document.QuerySelectorAll(".zip-member-chamber").Select(section => section.GetAttribute("data-chamber")));
        Assert.Contains("No current member is in our records.", document.QuerySelector(".zip-member-chamber[data-chamber=house]")!.TextContent);
        Assert.Contains("No current member is in our records.", document.QuerySelector(".zip-member-chamber[data-chamber=senate]")!.TextContent);
    }

    [Fact]
    public async Task MembersRouteRejectsRepeatedAndUnsupportedViewsBeforeLookup()
    {
        await using var host = await StartAsync();

        await AssertProblemAsync(host, "/v1/zip-districts/36009/members?view=compare&view=compare", HttpStatusCode.BadRequest);
        await AssertProblemAsync(host, "/v1/zip-districts/36009/members?view=" + new string('x', 512), HttpStatusCode.BadRequest);
        await AssertProblemAsync(host, "/v1/zip-districts/36009/members?view=Compare", HttpStatusCode.BadRequest);
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

    private static async Task AddSplitPartyLineAsync(string database, char id, int firstRelease, string officialId,
        string pinnedParty, int congress, string votedOn)
    {
        await ViewsSeed.AddVotesAsync(database, new SeededVote(id, votedOn)
        {
            Congress = congress,
            FirstRelease = firstRelease,
            Casts = ["Yea", "Yea", "Nay", "Nay"]
        });
        var listId = new string(id, 64);
        await ViewsSeed.ExecuteAsync(database, $"""
            SET session_replication_role = replica;
            UPDATE vote_member_list_entries SET person_id = CASE "position"
                WHEN 0 THEN '{officialId}' ELSE 'untracked-' || "position"::text END,
                party = CASE WHEN "position" < 2 THEN 'Democrat' ELSE 'Republican' END
             WHERE list_id = '{listId}';
            UPDATE vote_member_list_entries SET party = '{pinnedParty}'
             WHERE list_id = '{listId}' AND "position" = 0;
            UPDATE vote_revisions SET columns_written = true, alignment = 'Split', democrat_majority = 'Yea', republican_majority = 'Nay'
             WHERE record_id = '{listId}';
            """);
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
