using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.PublicWeb;

public sealed class MethodPageTests
{
    [Fact]
    public async Task MissedVoteMeaningNamesTheNewestCongressWithAttendanceFiguresForTheMember()
    {
        var document = await RenderAsync();

        var panel = Assert.Single(document.QuerySelectorAll("section"), section =>
            section.QuerySelector("h2")?.TextContent == "Missed votes");
        var paragraph = panel.QuerySelector("p")!;
        Assert.Equal(
            VoteWords.MissedVoteMeaning + " The count covers the newest Congress with attendance figures for that member, in each chamber where the official has votes.",
            paragraph.TextContent);
    }

    [Fact]
    public async Task VotingBoardDescriptionMatchesTheCurrentListsAndPreservesTheOfficialsLink()
    {
        var document = await RenderAsync();

        var panel = Assert.Single(document.QuerySelectorAll("section"), section =>
            section.QuerySelector("h2")?.TextContent == "The voting boards");
        Assert.Equal(
        [
            "Each voting board lists members of one party in one chamber who voted against most of their party at least once. Members rank by their share of split votes cast against most of the party. The list uses one Congress in each chamber. That is the newest Congress with vote figures in this release, once any member of the chamber has at least 25 split votes cast in it. Until then, it uses the next older Congress with vote figures when there is one. If there is none, it keeps the newest Congress. The page says which Congress it uses. The House and the Senate can show different Congresses.",
            "Only members with at least 25 split votes cast as Yes or No are ranked. Each row shows the count, the total and the share. A smaller total can come from a late start or from split votes the member skipped. Members with fewer split votes, no vote figures or no votes against their party are not listed.",
            "Delegates and the Resident Commissioner rank in their own list. Delegates may not vote on final passage in the full House, so they cast fewer split votes. They rank apart from voting members."
        ], panel.QuerySelectorAll("p").Where(paragraph => paragraph.QuerySelector("a") is null).Select(paragraph => paragraph.TextContent).ToArray());

        var link = Assert.Single(panel.QuerySelectorAll("a"));
        Assert.Equal("Open the boards from the officials page.", link.TextContent);
        Assert.Equal(ListPageRender.Root + PublicationProtocol.OfficialsPagePath, link.GetAttribute("href"));
    }

    private static Task<AngleSharp.Dom.IDocument> RenderAsync() =>
        ListPageRender.RenderPageAsync<MethodPage>(new Dictionary<string, object?>
        {
            [nameof(MethodPage.RootPath)] = ListPageRender.Root
        });
}
