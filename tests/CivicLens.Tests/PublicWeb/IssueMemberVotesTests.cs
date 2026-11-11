using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class IssueMemberVotesTests
{
    private const string Vacant = "No current member is in our records.";
    private const string NoVotes = "None of your members has a recorded vote on this issue in this release.";

    private static readonly ZipMemberIdentityData Pat = new("pat", "Pat Member", "Democratic", null, null, [], []);

    private static readonly SharedVoteRowsData HouseVotes = new(PublishedChamber.House, [new("pat", "Pat Member")],
        [new(new string('d', 64), new(2026, 9, 16), 119, 1, 5, "A vote", null, "Vote to pass", "Passed", [])]);

    [Fact]
    public async Task TheSharedVotesSitBelowTheMembersHeadingSoTheSectionIsAnH3AndItsRowsAreH4()
    {
        var page = await RenderAsync(Data([new("House seat", Pat)], HouseVotes));

        Assert.Equal("How your House members voted", page.QuerySelector("h3")!.TextContent);
        Assert.Equal("A vote", page.QuerySelector("h4 a")!.TextContent);
        Assert.Null(page.QuerySelector("h2"));
    }

    [Fact]
    public async Task AZipWhoseSeatsAreAllVacantSaysSoAndNotThatMembersHaveNoVote()
    {
        var page = await RenderAsync(Data([new("House seat", null), new("Senate seat", null) { Chamber = PublishedChamber.Senate }]));

        Assert.Equal([Vacant], page.QuerySelectorAll("[data-member-votes-empty]").Select(note => note.TextContent));
        Assert.DoesNotContain(NoVotes, page.DocumentElement.TextContent);
    }

    [Fact]
    public async Task AChamberWithOnlyVacantSeatsSaysSoBesideTheVotesOfTheOtherChamber()
    {
        var page = await RenderAsync(Data([new("House seat", Pat), new("Senate seat", null) { Chamber = PublishedChamber.Senate }], HouseVotes));

        Assert.Single(page.QuerySelectorAll(".shared-vote-rows"));
        Assert.Equal([Vacant], page.QuerySelectorAll("[data-member-votes-empty]").Select(note => note.TextContent));
        Assert.DoesNotContain(NoVotes, page.DocumentElement.TextContent);
    }

    [Fact]
    public async Task MembersWithNoVoteOnTheIssueKeepTheNoRecordedVoteMessage()
    {
        var page = await RenderAsync(Data([new("House seat", Pat)]));

        Assert.Equal([NoVotes], page.QuerySelectorAll("[data-member-votes-empty]").Select(note => note.TextContent));
    }

    private static ZipMemberCardsData Data(ZipMemberSeatData[] seats, params SharedVoteRowsData[] votes) =>
        new(7, 119, "36009", 1, seats, []) { SharedVotes = votes, SelectedIssue = "health" };

    private static async Task<IDocument> RenderAsync(ZipMemberCardsData data)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<IssueMemberVotes>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(IssueMemberVotes.Data)] = data,
                [nameof(IssueMemberVotes.RootPath)] = "https://civic-lens.test/"
            }));
            return output.ToHtmlString();
        });
        return new HtmlParser().ParseDocument(html);
    }
}
