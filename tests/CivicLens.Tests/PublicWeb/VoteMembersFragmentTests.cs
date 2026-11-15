using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class VoteMembersFragmentTests
{
    private static readonly string RecordId = new('a', 64);

    private static VoteMembersData Data(PublishedChamber chamber, int otherChamber, params VoteZipMemberData[] members) =>
        new("02139", RecordId, 7, chamber, members, otherChamber, false);

    private static VoteZipMemberData Member(string name, string? cast, string? party, bool? breaks) =>
        new("id-" + name, name, "Representative for Massachusetts, district 7", cast, party, breaks);

    [Fact]
    public async Task TheFragmentNamesTheZipTheVoteAndTheReleaseForTheScriptToCheck()
    {
        var root = (await RenderAsync(Data(PublishedChamber.House, 0, Member("Ann Two", "Yea", "Democrat", null)))).QuerySelector("[data-vote-members-fragment]")!;

        Assert.Equal("02139", root.GetAttribute("data-zcta"));
        Assert.Equal(RecordId, root.GetAttribute("data-record-id"));
        Assert.Equal("7", root.GetAttribute("data-release"));
    }

    [Fact]
    public async Task EachMemberShowsTheCastThePartyAndTheSeatAndOnlyABreakGetsTheHatchedMark()
    {
        var page = await RenderAsync(Data(PublishedChamber.House, 2,
            Member("Ann Two", "Nay", "Democrat", true),
            Member("Bob Three", "Yea", "Republican", false),
            Member("Cy Four", "Present", "Democrat", null),
            Member("Di Five", "Yea", null, null)));

        var items = page.QuerySelectorAll(".vote-zip-members li").ToArray();
        Assert.Equal(["Ann Two " + PlainVoteWords.Cast("Nay"), "Bob Three " + PlainVoteWords.Cast("Yea"), "Cy Four " + PlainVoteWords.Cast("Present"), "Di Five " + PlainVoteWords.Cast("Yea")],
            items.Select(item => item.QuerySelector(".vote-zip-member")!.TextContent));
        Assert.Equal(["nay", "yea", null, "yea"], items.Select(item => item.QuerySelector(".shared-vote-cast-value")!.GetAttribute("data-side")));
        Assert.Equal("Democrat. Representative for Massachusetts, district 7.", items[0].QuerySelector("p.muted")!.TextContent);
        Assert.Equal("No known party. Representative for Massachusetts, district 7.", items[3].QuerySelector("p.muted")!.TextContent);
        Assert.Equal(["Voted against most Democrats", "Voted with most Republicans"], page.QuerySelectorAll(".vote-zip-member-line").Select(line => line.TextContent));
        var mark = Assert.Single(page.QuerySelectorAll(".vote-zip-member-line .swatch.against"));
        Assert.Same(items[0], mark.Closest("li"));
        Assert.Equal("democrat", mark.GetAttribute("data-tone"));
        Assert.Equal("true", mark.GetAttribute("aria-hidden"));
        Assert.Equal(["Your senators do not vote in the House."], page.QuerySelectorAll("[data-vote-members-fragment] > p").Select(note => note.TextContent));
    }

    [Fact]
    public async Task AMemberWithNoCastHasNoPartyNoMarkAndNoRecordedPosition()
    {
        var page = await RenderAsync(Data(PublishedChamber.Senate, 1, Member("Sue Six", null, null, null) with { SeatTitle = "Senator for Massachusetts" }));

        var item = page.QuerySelector(".vote-zip-members li")!;
        Assert.Null(item.QuerySelector(".shared-vote-cast-value"));
        Assert.Null(item.QuerySelector(".vote-zip-member-line"));
        Assert.Equal(["Sue Six", "Senator for Massachusetts.", "Sue Six has no recorded position on this vote."], item.QuerySelectorAll("p").Select(line => line.TextContent));
        Assert.Equal(["Your House member does not vote in the Senate."], page.QuerySelectorAll("[data-vote-members-fragment] > p").Select(note => note.TextContent));
    }

    [Fact]
    public async Task AChamberWithOnlyVacantSeatsSaysSoAndAZipWithNoOtherChamberMemberHasNoChamberNote()
    {
        var page = await RenderAsync(new("02139", RecordId, 7, PublishedChamber.House, [], 0, true));

        Assert.Null(page.QuerySelector(".vote-zip-members"));
        Assert.Equal([ZipMemberCard.NoCurrentMember], page.QuerySelectorAll("[data-vote-members-fragment] > p").Select(note => note.TextContent));
    }

    private static async Task<AngleSharp.Dom.IDocument> RenderAsync(VoteMembersData data)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<VoteMembersFragment>(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(VoteMembersFragment.Data)] = data }))).ToHtmlString());
        return await new HtmlParser().ParseDocumentAsync(html);
    }
}
