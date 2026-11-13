using System.Net;
using AngleSharp.Html.Parser;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.ZipDistricts;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class BillMembersEndpointsTests(PostgresCollection postgres)
{
    private static readonly string BillId = new('a', 64);

    private async Task<ApiHost> StartAsync(params SeededSponsorship[] sponsorships)
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
            await BillsSeed.AddBillsAsync(host.OperationalDatabase, new SeededBill('a', "2026-03-02") { BillNumber = 7, Sponsorships = sponsorships });
            await ViewsSeed.AddVotesAsync(host.OperationalDatabase,
                new SeededVote('e', "2026-03-03") { BillNumber = 7, Casts = ["Nay"], PersonIds = ["al2"] },
                new SeededVote('f', "2026-03-04") { BillNumber = 8, Casts = ["Yea"], PersonIds = ["al2"] });
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    [Fact]
    public async Task TheFragmentShowsEachMemberRelationTheirCastOnTheBillVoteAndTheSenateGapWithoutCastsOnOtherBillVotes()
    {
        await using var host = await StartAsync(new SeededSponsorship("al2", "Cosponsor", "2026-03-05", null, false));

        var response = await host.Client.GetAsync($"/v1/zip-districts/36009/bills/{BillId}");
        var page = new HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store, private", response.Headers.CacheControl!.ToString());
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType!.ToString());
        var root = page.QuerySelector("[data-bill-members-fragment]")!;
        Assert.Equal("36009", root.GetAttribute("data-zcta"));
        Assert.Equal(BillId, root.GetAttribute("data-record-id"));
        Assert.Equal("1", root.GetAttribute("data-release"));
        Assert.Equal(
            ["Alice House cosponsored it on 5 Mar 2026.", "Alice House voted No (Nay) on the House vote of 3 Mar 2026.", "Sam Senator did not sponsor or cosponsor it."],
            page.QuerySelectorAll(".bill-members-list p").Select(item => item.TextContent));
        Assert.Equal(["Your senator has no Senate vote on this bill in this release."], page.QuerySelectorAll("p.muted").Select(item => item.TextContent));
    }

    [Fact]
    public async Task CastsAndTheSenateVoteOlderThanTheFirstFiftyHouseVotesStillShow()
    {
        await using var host = await StartAsync(new SeededSponsorship("p9", "Sponsor", "2026-03-02"));
        var newer = Enumerable.Range(1, PublicationProtocol.PageSize + 1)
            .Select(index => new SeededVote('d', "2026-04-01") { BillNumber = 7, RollCall = 100 + index, RecordId = (100 + index).ToString("x64") });
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, [.. newer,
            new SeededVote('c', "2026-03-01") { BillNumber = 7, RollCall = 99, RecordId = 1.ToString("x64"), Casts = ["Yea"], PersonIds = ["al2"] },
            new SeededVote('b', "2026-02-01") { Chamber = "Senate", BillNumber = 7, RecordId = 2.ToString("x64"), Casts = ["Nay"], PersonIds = ["al-sen"] }]);

        var page = new HtmlParser().ParseDocument(await host.Client.GetStringAsync($"/v1/zip-districts/36009/bills/{BillId}"));

        Assert.Equal(
            ["Alice House did not sponsor or cosponsor it.", "Alice House voted Yes (Yea) on the House vote of 1 Mar 2026.", "Alice House voted No (Nay) on the House vote of 3 Mar 2026.",
             "Sam Senator did not sponsor or cosponsor it.", "Sam Senator voted No (Nay) on the Senate vote of 1 Feb 2026."],
            page.QuerySelectorAll(".bill-members-list p").Select(item => item.TextContent));
        Assert.Empty(page.QuerySelectorAll("p.muted"));
    }

    [Fact]
    public async Task AWithdrawnCosponsorshipIsNotARelation()
    {
        await using var host = await StartAsync(new SeededSponsorship("al2", "Cosponsor", "2026-03-05", "2026-03-06", false));

        var page = new HtmlParser().ParseDocument(await host.Client.GetStringAsync($"/v1/zip-districts/36009/bills/{BillId}"));

        Assert.Equal("Alice House did not sponsor or cosponsor it.", page.QuerySelector(".bill-members-list p")!.TextContent);
    }

    [Fact]
    public async Task AnUnknownBillAnUnknownZipAndABadRecordIdAreNotFoundAndNothingIsCached()
    {
        await using var host = await StartAsync();

        foreach (var path in new[] { $"/v1/zip-districts/36009/bills/{new string('9', 64)}", $"/v1/zip-districts/00000/bills/{BillId}", "/v1/zip-districts/36009/bills/not-an-id" })
        {
            var response = await host.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("no-store, private", response.Headers.CacheControl!.ToString());
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync($"/v1/zip-districts/123/bills/{BillId}")).StatusCode);
    }
}
