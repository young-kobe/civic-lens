using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Publication;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.ZipDistricts;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class OfficialMembersEndpointsTests(PostgresCollection postgres)
{
    private const string Representative = "Representative";
    private const string Senator = "Senator";

    // ZIP 12345 lies in Alabama districts 1 and 2. Release 1 holds one representative for each, and the two senators of Alabama.
    private async Task<ApiHost> StartAsync()
    {
        var host = await ApiHost.StartAsync(postgres);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await ZipSeed.AddRecordAsync(host.OperationalDatabase, 1);
        await ZipSeed.AdmitDistrictsAsync(host.OperationalDatabase);
        await ZipSeed.AddNamesAsync(host.OperationalDatabase, 1,
            new SeededSeat("p0", "Rita One", Representative, "AL", 1),
            new SeededSeat("p1", "Ross Two", Representative, "AL", 2),
            new SeededSeat("p2", "Sam Senator", Senator, "AL", null),
            new SeededSeat("p3", "Sue Senator", Senator, "AL", null),
            new SeededSeat("ak0", "Ann Large", Representative, "AK", 0),
            new SeededSeat("ak-a", "Al First", Senator, "AK", null),
            new SeededSeat("ak-b", "Bea Second", Senator, "AK", null));
        // Roll call 1: the two representatives differ. Roll call 2: they agree. Roll call 3: the senators differ.
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase,
            new SeededVote('1', "2026-02-01") { RecordId = 1.ToString("x64"), RollCall = 1, Casts = ["Yea", "Nay"], PersonIds = ["p0", "p1"] },
            new SeededVote('2', "2026-02-02") { RecordId = 2.ToString("x64"), RollCall = 2, Casts = ["Yea", "Yea"], PersonIds = ["p0", "p1"] },
            new SeededVote('3', "2026-02-03") { RecordId = 3.ToString("x64"), Chamber = "Senate", RollCall = 3, Casts = ["Yea", "Nay"], PersonIds = ["p2", "p3"] });
        return host;
    }

    private static async Task<IDocument> GetFragmentAsync(ApiHost host, string query, string zcta = "12345")
    {
        using var response = await host.Client.GetAsync($"/v1/zip-districts/{zcta}/members?" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl is { NoStore: true, Private: true });
        return new HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AMemberOfTheZipNamesTheOtherMembersLinksCompareAndListsOnlyTheVotesWhereTheMembersOfTheChamberDiffered()
    {
        await using var host = await StartAsync();

        var fragment = await GetFragmentAsync(host, "view=official&official=p0");

        var root = fragment.QuerySelector("[data-zip-member-fragment]")!;
        Assert.Equal("official", root.GetAttribute("data-view"));
        Assert.Equal("p0", root.GetAttribute("data-official"));
        var note = fragment.QuerySelector(".official-members-note")!;
        Assert.StartsWith("Rita One is one of your members. Your other members: Ross Two, Sam Senator and Sue Senator.", note.TextContent.Trim().ReplaceLineEndings(" ").Replace("  ", " "));
        Assert.Equal(["Ross Two", "Sam Senator", "Sue Senator"], note.QuerySelectorAll("a").Take(3).Select(link => link.TextContent));
        Assert.Equal("Compare all 4", note.QuerySelectorAll("a").Last().TextContent);
        Assert.EndsWith("/compare.html", note.QuerySelectorAll("a").Last().GetAttribute("href"));
        Assert.Equal("Your ZIP area lies in 2 districts. The district with the most land is first.", note.NextElementSibling!.TextContent);
        var votes = fragment.QuerySelector("section.shared-vote-rows[data-chamber='house']")!;
        Assert.Equal("Votes where your members differed", votes.QuerySelector("h3")!.TextContent);
        Assert.Single(votes.QuerySelectorAll(".shared-vote-row"));
        Assert.EndsWith("/" + 1.ToString("x64") + ".html", votes.QuerySelector(".shared-vote-row h4 a")!.GetAttribute("href"));
        Assert.Null(fragment.QuerySelector("section.shared-vote-rows[data-chamber='senate']"));
    }

    [Fact]
    public async Task AMemberWhoIsTheOnlyMemberOfTheChamberGetsTheNoteAndNoComparisonOrEmptyMessage()
    {
        await using var host = await StartAsync();

        var fragment = await GetFragmentAsync(host, "view=official&official=ak0", "99501");

        Assert.StartsWith("Ann Large is one of your members. Your other members: Al First and Bea Second.", fragment.QuerySelector(".official-members-note")!.TextContent.Trim().ReplaceLineEndings(" ").Replace("  ", " "));
        Assert.Null(fragment.QuerySelector(".shared-vote-rows"));
        Assert.Null(fragment.QuerySelector("[data-member-votes-empty]"));
        Assert.Null(fragment.QuerySelector(".zip-count"));
    }

    [Fact]
    public async Task AChamberWithNoSharedVotesShowsTheSameEmptyLineOnTheOfficialPageAsOnCompare()
    {
        await using var host = await StartAsync();

        var official = await GetFragmentAsync(host, "view=official&official=ak-a", "99501");
        var compare = await GetFragmentAsync(host, "view=compare", "99501");

        Assert.Equal(SharedVoteRows.NoDifferences, official.QuerySelector("[data-member-votes-empty]")!.TextContent);
        Assert.Equal(SharedVoteRows.NoDifferences, compare.QuerySelector(".zip-member-chamber[data-chamber=senate] [data-member-votes-empty]")!.TextContent);
    }

    [Fact]
    public async Task AnOfficialWhoIsNotAMemberOfTheZipGetsAnEmptyFragmentSoThePageHidesItsSection()
    {
        await using var host = await StartAsync();

        var fragment = await GetFragmentAsync(host, "view=official&official=nobody");

        var root = fragment.QuerySelector("[data-zip-member-fragment]")!;
        Assert.Equal("nobody", root.GetAttribute("data-official"));
        Assert.Equal(0, root.ChildElementCount);
    }

    [Fact]
    public async Task TheOfficialViewNeedsAnOfficialAndTakesNoIssueNoReleaseMarkerAndNoOtherViewMayNameAnOfficial()
    {
        await using var host = await StartAsync();

        foreach (var query in new[] { "view=official", "view=official&official=p0&issue=tax", "view=official&official=p0&sinceRelease=1", "view=compare&official=p0", "official=p0" })
        {
            using var response = await host.Client.GetAsync("/v1/zip-districts/12345/members?" + query);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{query} gave {(int)response.StatusCode}");
        }
    }
}
