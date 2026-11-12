using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class BillMembersFragmentTests
{
    private static readonly string RecordId = new('a', 64);
    private static readonly BillMemberCastData[] NoCasts = [];

    private static BillMembersData Data(PublishedChamber[] voted, params BillMemberData[] members) => new("02139", RecordId, 7, members, voted);

    [Fact]
    public async Task TheFragmentNamesTheZipTheBillAndTheReleaseForTheScriptToCheck()
    {
        var root = (await RenderAsync(Data([], new BillMemberData("Ann Two", PublishedChamber.House, BillMemberRelation.None, null, NoCasts)))).QuerySelector("[data-bill-members-fragment]")!;

        Assert.Equal("02139", root.GetAttribute("data-zcta"));
        Assert.Equal(RecordId, root.GetAttribute("data-record-id"));
        Assert.Equal("7", root.GetAttribute("data-release"));
    }

    [Fact]
    public async Task EachMemberShowsTheirRelationAndOnlyACosponsorShowsTheJoinDay()
    {
        var page = await RenderAsync(Data([PublishedChamber.House],
            new BillMemberData("Ann Two", PublishedChamber.House, BillMemberRelation.Sponsor, null, NoCasts),
            new BillMemberData("Bob Three", PublishedChamber.House, BillMemberRelation.Cosponsor, new(2025, 9, 30), NoCasts),
            new BillMemberData("Cy Four", PublishedChamber.House, BillMemberRelation.None, null, NoCasts)));

        Assert.Equal(["Ann Two sponsored it.", "Bob Three cosponsored it on 30 Sep 2025.", "Cy Four did not sponsor or cosponsor it."],
            page.QuerySelectorAll(".bill-members-list li p").Select(item => item.TextContent));
    }

    [Fact]
    public async Task ACastShowsTheSideOfTheCastTheChamberAndTheDayOfTheVote()
    {
        var page = await RenderAsync(Data([PublishedChamber.House],
            new BillMemberData("Ann Two", PublishedChamber.House, BillMemberRelation.None, null, [new(new(2026, 9, 16), "No", "Nay")])));

        var cast = page.QuerySelector(".bill-members-list .shared-vote-cast-value")!;
        Assert.Equal("nay", cast.GetAttribute("data-side"));
        Assert.Equal("Ann Two voted No on the House vote of 16 Sep 2026.", cast.ParentElement!.TextContent);
    }

    [Fact]
    public async Task AChamberWithMembersAndNoVoteOnTheBillGetsOneNoVoteLineAndAChamberWithAVoteGetsNone()
    {
        var members = new BillMemberData[]
        {
            new BillMemberData("Ann Two", PublishedChamber.House, BillMemberRelation.None, null, NoCasts),
            new BillMemberData("Sam One", PublishedChamber.Senate, BillMemberRelation.None, null, NoCasts),
            new BillMemberData("Sue Two", PublishedChamber.Senate, BillMemberRelation.None, null, NoCasts)
        };

        var senateOnly = await RenderAsync(Data([PublishedChamber.House], members));
        var both = await RenderAsync(Data([PublishedChamber.House, PublishedChamber.Senate], members));
        var none = await RenderAsync(Data([], members));

        Assert.Equal(["Your senators have no Senate vote on this bill in this release."], senateOnly.QuerySelectorAll("p.muted").Select(item => item.TextContent));
        Assert.Empty(both.QuerySelectorAll("p.muted"));
        Assert.Equal(["Your House member has no House vote on this bill in this release.", "Your senators have no Senate vote on this bill in this release."],
            none.QuerySelectorAll("p.muted").Select(item => item.TextContent));
    }

    private static async Task<AngleSharp.Dom.IDocument> RenderAsync(BillMembersData data)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<BillMembersFragment>(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(BillMembersFragment.Data)] = data }))).ToHtmlString());
        return await new HtmlParser().ParseDocumentAsync(html);
    }
}
