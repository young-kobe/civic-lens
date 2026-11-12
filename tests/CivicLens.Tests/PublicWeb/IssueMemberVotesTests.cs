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
        [new(new string('d', 64), new(2026, 9, 16), 119, 1, 5, "A vote", null, "Vote to pass", "Passed", [new("pat", "Yes", "Yea")])]);

    [Fact]
    public async Task EachMemberGetsAnH3NameAndSingleMemberVoteRowsWithAnH4HeadingAndNoRepeatedName()
    {
        var page = await RenderAsync(Data([new("House seat", Pat)], HouseVotes));

        Assert.Equal("Pat Member", page.QuerySelector("h3")!.TextContent);
        Assert.Equal("A vote", page.QuerySelector("h4 a")!.TextContent);
        Assert.Null(page.QuerySelector("h2"));
        Assert.Single(page.QuerySelectorAll(".shared-vote-rows.single"));
        Assert.Null(page.QuerySelector(".shared-vote-member"));
        Assert.Null(page.QuerySelector(".shared-vote-caption"));
    }

    [Fact]
    public async Task TwoMembersOfOneChamberGetTheirOwnSectionWithOnlyTheirOwnCast()
    {
        var other = new SharedVoteMemberData("sam", "Sam Member");
        var votes = new SharedVoteRowsData(PublishedChamber.Senate, [new("pat", "Pat Member"), other],
            [HouseVotes.Votes[0] with { Casts = [new("pat", "Yes", "Yea"), new("sam", "No", "Nay")] },
             HouseVotes.Votes[0] with { RecordId = new string('e', 64), Casts = [new("pat", "No", "Nay")] }]);

        var page = await RenderAsync(Data([new("Senate seat", Pat) { Chamber = PublishedChamber.Senate }], votes));

        var sections = page.QuerySelectorAll(".issue-member");
        Assert.Equal(["Pat Member", "Sam Member"], sections.Select(section => section.QuerySelector("h3")!.TextContent));
        Assert.Equal([2, 1], sections.Select(section => section.QuerySelectorAll(".shared-vote-row").Length));
        Assert.Equal(["No"], sections[1].QuerySelectorAll(".shared-vote-cast").Select(cast => cast.TextContent.Trim()));
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
