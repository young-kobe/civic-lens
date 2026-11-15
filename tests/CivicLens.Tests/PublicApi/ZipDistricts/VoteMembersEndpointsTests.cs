using System.Net;
using AngleSharp.Html.Parser;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.ZipDistricts;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class VoteMembersEndpointsTests(PostgresCollection postgres)
{
    // Most Democrats on the House vote cast Yes, so the No of Alice House breaks from the party. The Senate vote is not split, so it gives no party line.
    private static readonly SeededVote House = new('e', "2026-03-03") { Casts = ["Nay", "Yea", "Yea"], PersonIds = ["al2", "x1", "x2"] };
    private static readonly SeededVote Senate = new('f', "2026-03-04") { Chamber = "Senate", Casts = ["Yea", "Nay"], PersonIds = ["al-sen", "x3"] };
    private static readonly SeededVote Without = new('d', "2026-03-05") { Casts = ["Yea"], PersonIds = ["x1"] };

    private async Task<ApiHost> StartAsync()
    {
        var host = await ApiHost.StartAsync(postgres);
        try
        {
            await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
            await ZipSeed.AddRecordAsync(host.OperationalDatabase, 1);
            await ZipSeed.AdmitDistrictsAsync(host.OperationalDatabase);
            await ZipSeed.AddNamesAsync(host.OperationalDatabase, 1,
                new SeededSeat("al2", "Alice House", "Representative", "AL", 2),
                new SeededSeat("al-sen", "Sam Senator", "Senator", "AL", null));
            await ViewsSeed.AddVotesAsync(host.OperationalDatabase, House, Senate, Without);
            await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
                UPDATE vote_member_list_entries SET party = 'Democrat';
                UPDATE vote_revisions SET alignment = 'Split', democrat_majority = 'Yea', republican_majority = 'Nay', columns_written = true WHERE record_id = '{House.RecordId}';
                """);
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    private static async Task<AngleSharp.Dom.IDocument> GetAsync(ApiHost host, SeededVote vote) =>
        new HtmlParser().ParseDocument(await host.Client.GetStringAsync($"/v1/zip-districts/36009/votes/{vote.RecordId}"));

    [Fact]
    public async Task TheFragmentShowsTheCastThePartyTheSeatAndTheBreakMarkOfTheViewAndNamesTheSenatorWhoDoesNotVote()
    {
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync($"/v1/zip-districts/36009/votes/{House.RecordId}");
        var page = new HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store, private", response.Headers.CacheControl!.ToString());
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType!.ToString());
        var root = page.QuerySelector("[data-vote-members-fragment]")!;
        Assert.Equal("36009", root.GetAttribute("data-zcta"));
        Assert.Equal(House.RecordId, root.GetAttribute("data-record-id"));
        Assert.Equal("1", root.GetAttribute("data-release"));
        var member = Assert.Single(page.QuerySelectorAll(".vote-zip-members li"));
        Assert.Equal("Alice House " + PlainVoteWords.Cast("Nay"), member.QuerySelector(".vote-zip-member")!.TextContent);
        Assert.Equal("nay", member.QuerySelector(".shared-vote-cast-value")!.GetAttribute("data-side"));
        Assert.Equal("Democrat. Representative for Alabama, district 2.", member.QuerySelector("p.muted")!.TextContent);
        Assert.Equal(["al2|true"], await ViewsSeed.QueryAsPublicAsync(host.OperationalDatabase,
            $"SELECT official_id || '|' || breaks_party::text FROM public_api.member_votes WHERE record_id = '{House.RecordId}' AND official_id = 'al2'"));
        Assert.Equal("Voted against most Democrats", member.QuerySelector(".vote-zip-member-line")!.TextContent);
        Assert.Equal("democrat", member.QuerySelector(".vote-zip-member-line .swatch.against")!.GetAttribute("data-tone"));
        Assert.Equal(["Your senator does not vote in the House."], page.QuerySelectorAll("[data-vote-members-fragment] > p").Select(note => note.TextContent));
        Assert.DoesNotContain("Sam Senator", root.TextContent);
    }

    [Fact]
    public async Task AVoteThatIsNotSplitGivesNoPartyLineAndAMemberWithoutACastHasNoRecordedPosition()
    {
        await using var host = await StartAsync();

        var senate = await GetAsync(host, Senate);
        var without = await GetAsync(host, Without);

        Assert.Equal("Sam Senator " + PlainVoteWords.Cast("Yea"), senate.QuerySelector(".vote-zip-member")!.TextContent);
        Assert.Null(senate.QuerySelector(".vote-zip-member-line"));
        Assert.Equal(["Your House member does not vote in the Senate."], senate.QuerySelectorAll("[data-vote-members-fragment] > p").Select(note => note.TextContent));
        var member = without.QuerySelector(".vote-zip-members li")!;
        Assert.Null(member.QuerySelector(".shared-vote-cast-value"));
        Assert.Equal("Representative for Alabama, district 2.", member.QuerySelector("p.muted")!.TextContent);
        Assert.Equal("Alice House has no recorded position on this vote.", member.QuerySelector("p:not(.muted):not(.vote-zip-member)")!.TextContent);
    }

    [Fact]
    public async Task AnUnknownVoteAnUnknownZipAndABadRecordIdAreNotFoundAndNothingIsCached()
    {
        await using var host = await StartAsync();

        foreach (var path in new[] { $"/v1/zip-districts/36009/votes/{new string('9', 64)}", $"/v1/zip-districts/00000/votes/{House.RecordId}", "/v1/zip-districts/36009/votes/not-an-id" })
        {
            var response = await host.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("no-store, private", response.Headers.CacheControl!.ToString());
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync($"/v1/zip-districts/123/votes/{House.RecordId}")).StatusCode);
    }
}
