using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Publication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.PublicWeb.VoteDetailPageFixture;

namespace CivicLens.Tests.PublicWeb;

public sealed class VoteMapPageTests
{
    private static VoteMemberData Member(string id, string name, string cast, string? party, string state, int? district) =>
        new(id, name, cast, party, Cite(Lines[4]), state, district);

    private static readonly VoteMemberData[] Members =
    [
        Member("o1", "Ada Able", "Aye", "Democrat", "MA", 7),
        Member("o2", "Ben Baker", "Nay", "Democrat", "MA", 5),
        Member("o3", "Cy Cole", "Aye", "Republican", "TX", 1),
        Member("o4", "Di Dole", "Not Voting", "Republican", "TX", 2),
        Member("o5", "Ida Free", "Nay", "Independent", "VT", 0),
        Member("d1", "Dee Cee", "Present", "Democrat", "DC", 0)
    ];

    private static VotePageData House(params VoteMemberData[] members) => Vote() with { Members = members };

    private static async Task<IDocument> RenderPageAsync(VotePageData data)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<VoteMapPage>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(VoteMapPage.Data)] = data,
                [nameof(VoteMapPage.RootPath)] = Root
            }));
            return output.ToHtmlString();
        });
        return await new HtmlParser().ParseDocumentAsync(html);
    }

    [Fact]
    public async Task TheMapPageNamesTheVoteLinksBackToItAndEndsTheTrailWithMap()
    {
        var page = await RenderPageAsync(House(Members));

        Assert.Equal("How the House voted: roll call 308, H.R. 5334", page.QuerySelector("h1")!.TextContent);
        Assert.Contains("House vote, 16 Sep 2026 | Map", page.QuerySelector(".page-header-eyebrow")!.TextContent);
        Assert.Equal(Root + PublicationProtocol.RecordPagePath(RecordId),
            page.QuerySelectorAll(".page-header a").Single(link => link.TextContent == VoteMapWords.BackToVote).GetAttribute("href"));
        var crumbs = page.QuerySelectorAll(".public-breadcrumbs li").ToArray();
        Assert.Equal(["Home", "Votes", "House roll call 308", "Map"], crumbs.Select(item => item.TextContent.Replace("/", "").Trim()));
        Assert.Equal(Root + PublicationProtocol.RecordPagePath(RecordId), crumbs[2].QuerySelector("a")!.GetAttribute("href"));
        Assert.Null(crumbs[3].QuerySelector("a"));
        Assert.Equal(VoteMapWords.RecordedVotesOnly, page.QuerySelector(".notice")!.TextContent);
    }

    [Fact]
    public async Task BothColorModesHaveTheirOwnLegendAndTheMapKeepsTheTableFallback()
    {
        var page = await RenderPageAsync(House(Members));

        var colors = page.QuerySelector("[data-vote-map-colors]")!;
        Assert.Equal(VoteMapWords.ColorGroup, page.GetElementById(colors.GetAttribute("aria-labelledby")!)!.TextContent);
        Assert.Equal([VoteMapWords.ByVote, VoteMapWords.ByParty], colors.QuerySelectorAll("button").Select(button => button.TextContent));
        Assert.Equal(["true", "false"], colors.QuerySelectorAll("button").Select(button => button.GetAttribute("aria-pressed")));
        Assert.Equal(["vote", "party"], colors.QuerySelectorAll("button").Select(button => button.GetAttribute("data-color")));
        Assert.True(colors.HasAttribute("hidden"));

        Assert.Equal(["2 Yes", "2 No", "1 Present", "1 Not voting", "430 No member in the record"],
            page.QuerySelectorAll(".vote-map-legend-vote li").Select(item => item.TextContent.Trim()));
        Assert.Equal(["Democrats: 1 Yes, 1 No, 1 Present", "Republicans: 1 Yes, 1 Not voting", "Independents: 1 No", "430 No member in the record"],
            page.QuerySelectorAll(".vote-map-legend-party li").Select(item => item.TextContent.Trim()));
        Assert.Equal("Color shows the member's party. A solid fill means Yes, stripes mean No, small dots mean Present, and rings mean the member did not vote. The counts add up to 436 seats and delegate circles.",
            page.QuerySelector(".vote-map-legend-party p")!.TextContent);
        var noDelegate = await RenderPageAsync(House(Members[..^1]));
        Assert.Equal("Color shows the member's party. A solid fill means Yes, stripes mean No, small dots mean Present, and rings mean the member did not vote. The counts add up to 435 seats.",
            noDelegate.QuerySelector(".vote-map-legend-party p")!.TextContent);

        var table = page.QuerySelector(".vote-map details.vote-fold table")!;
        Assert.Equal(436, table.QuerySelectorAll("tbody tr").Length);
        Assert.Null(page.QuerySelector("noscript"));
    }

    [Fact]
    public async Task EverySeatCarriesItsPartyAndTheLegendOfEachModeAddsUpToTheSameTotal()
    {
        var page = await RenderPageAsync(House(Members));

        Assert.Equal("democrat", page.QuerySelectorAll(".vote-map-shapes .vote-map-seat").Single(seat => seat.QuerySelector("title")!.TextContent.StartsWith("Massachusetts district 7:", StringComparison.Ordinal)).GetAttribute("data-party"));
        Assert.Equal("neutral", page.QuerySelectorAll(".vote-map-tiles .vote-map-seat").Single(seat => seat.QuerySelector("title")!.TextContent.StartsWith("Vermont at large:", StringComparison.Ordinal)).GetAttribute("data-party"));
        int Total(string selector) => page.QuerySelectorAll(selector + " li").Sum(item => System.Text.RegularExpressions.Regex.Matches(item.TextContent, @"\d+").Sum(match => int.Parse(match.Value)));
        Assert.Equal(436, Total(".vote-map-legend-vote"));
        Assert.Equal(436, Total(".vote-map-legend-party"));
    }

    [Fact]
    public async Task ThePartyModePaintsYesSolidNoStripedPresentDottedAndNotVotingRingedForEachPartyInCss()
    {
        var css = WebStyleSheets.Read("public.css");
        foreach (var party in new[] { "democrat", "republican", "neutral" })
        {
            Assert.Contains($".vote-map[data-color=\"party\"] .vote-map-seat[data-mark=\"no\"][data-party=\"{party}\"] {{ fill: url(#vote-map-party-no-{party}); }}", css);
            Assert.Contains($".vote-map[data-color=\"party\"] .vote-map-seat[data-mark=\"present\"][data-party=\"{party}\"] {{ fill: url(#vote-map-party-present-{party}); }}", css);
            Assert.Contains($".vote-map[data-color=\"party\"] .vote-map-seat[data-mark=\"not-voting\"][data-party=\"{party}\"] {{ fill: url(#vote-map-party-nv-{party}); }}", css);
        }
        Assert.Contains(".vote-map[data-color=\"party\"] .vote-map-seat[data-mark=\"yes\"] { fill: var(--tone); }", css);
        Assert.Contains(".vote-map-defs [data-party] .vote-map-ring { stroke: var(--tone); }", css);
    }

    [Fact]
    public async Task InThePartyModePresentAndNotVotingKeepTheTwoShapesOfTheVoteModeSoColorIsNeverTheOnlyCue()
    {
        var page = await RenderPageAsync(House(Members));

        string Shape(string id) => page.GetElementById(id)!.QuerySelector("circle")!.OuterHtml;
        foreach (var party in new[] { "democrat", "republican", "neutral" })
        {
            Assert.NotNull(page.GetElementById($"vote-map-party-no-{party}"));
            Assert.Equal(Shape("vote-map-present"), Shape($"vote-map-party-present-{party}"));
            Assert.Equal(Shape("vote-map-not-voting"), Shape($"vote-map-party-nv-{party}"));
        }
        Assert.NotEqual(Shape("vote-map-present"), Shape("vote-map-not-voting"));
    }

    [Fact]
    public async Task AVoteThatCannotBeMappedShowsTheEmptyStateAndNoMap()
    {
        var page = await RenderPageAsync(House(Member("o1", "Ada Able", "Aye", "Democrat", "MA", 7)) with { Congress = 118 });

        Assert.Null(page.QuerySelector(".vote-map"));
        Assert.Equal(VoteMapWords.NoMapTitle, page.QuerySelector(".empty h3")!.TextContent);
        Assert.Equal(["H1", "H2", "H3"], page.QuerySelectorAll("main :is(h1, h2, h3, h4)").Select(heading => heading.TagName));
        Assert.Equal(VoteMapWords.Heading, page.QuerySelector("main h2")!.TextContent);
        Assert.Contains(VoteMapWords.NoMap, page.QuerySelector(".empty")!.TextContent);
        Assert.Equal(Root + PublicationProtocol.RecordPagePath(RecordId), page.QuerySelector(".empty a")!.GetAttribute("href"));
        Assert.Equal("How the House voted: roll call 308, H.R. 5334", page.QuerySelector("h1")!.TextContent);
    }

    [Fact]
    public async Task ASeatWithNoPartyReadsAsNeutralAndHasItsOwnLegendRow()
    {
        var page = await RenderPageAsync(House(Member("o1", "Ada Able", "Aye", null, "MA", 7)));

        Assert.Equal("1 Yes", page.QuerySelectorAll(".vote-map-legend-party li").First().TextContent.Split(':')[1].Trim());
        Assert.Equal(VoteWords.NoKnownParty, page.QuerySelector(".vote-map-legend-party li b")!.TextContent);
        Assert.Equal("neutral", page.QuerySelector(".vote-map-legend-party svg rect")!.GetAttribute("data-party"));
    }
}
