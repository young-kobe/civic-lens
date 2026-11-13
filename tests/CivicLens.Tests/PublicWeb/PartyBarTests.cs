using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Components.Ui;
using CivicLens.PublicWeb.Publication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class PartyBarTests
{
    [Fact]
    public void TheDescriptionNamesTheGroupAndEverySegmentCount()
    {
        var bar = VotePageView.CastBar("Group", PartyTone.Neutral, 1, 2, 3, 4, 5, VoteThreshold.None);

        Assert.Equal("Group: Yes 1, No 2, Present 3, Other 5, Not voting 4", bar.Description);
        Assert.Equal(15, bar.Total);
    }

    [Theory]
    [InlineData(58, 152, 105.0)]
    [InlineData(3, 3, 3.0)]
    [InlineData(1, 0, 0.5)]
    public void TheHalfLineSitsAtHalfOfTheYesAndNoCasts(int yes, int no, double expected)
    {
        Assert.Equal(expected, VotePageView.CastBar("Group", PartyTone.Neutral, yes, no, 0, 4, 0, VoteThreshold.Half).Mark!.Count);
    }

    [Fact]
    public void NobodyCastingYesOrNoGivesNoLineAndAnOffSwitchGivesNoLine()
    {
        Assert.Null(VotePageView.CastBar("Group", PartyTone.Neutral, 0, 0, 2, 3, 0, VoteThreshold.Half).Mark);
        Assert.Null(VotePageView.CastBar("Group", PartyTone.Neutral, 5, 1, 0, 0, 0, VoteThreshold.None).Mark);
    }

    [Theory]
    [InlineData("Democrat", PartyTone.Democrat, 0)]
    [InlineData("Republican", PartyTone.Republican, 1)]
    [InlineData("Independent", PartyTone.Neutral, 2)]
    public void TheOneColorAndOrderRuleGivesDemocratsBlueRepublicansRedAndAnyoneElseNeutral(string party, PartyTone tone, int rank)
    {
        Assert.Equal(tone, PartyTones.Of(party));
        Assert.Equal(rank, PartyTones.Rank(party));
    }

    [Fact]
    public async Task EachSegmentHasItsOwnLabelAndLookInTheLegendSoNoTwoLabelsShareALookAndColorIsNotTheOnlyCue()
    {
        var html = await RenderAsync(VotePageView.CastBar("Democrats", PartyTone.Democrat, 3, 2, 1, 4, 5, VoteThreshold.Half), "bar");
        var page = await new HtmlParser().ParseDocumentAsync(html);

        var items = page.QuerySelectorAll(".party-bar-legend li");
        Assert.Equal(["Yes", "No", "Present", "Other", "Not voting", "Half of Yes and No votes: 2.5"],
            items.Select(item => Regex.Replace(item.TextContent, @"^\s*\d*\s*", "").Trim()));
        var looks = items.Take(5).Select(item => item.QuerySelector("rect")!).Select(rect => (rect.GetAttribute("data-tone"), rect.GetAttribute("data-fill"))).ToArray();
        Assert.Equal(5, looks.Distinct().Count());
        Assert.Equal(["solid", "stripes", "dots", "grid", "open"], looks.Select(look => look.Item2));
        Assert.Equal("quiet", looks[2].Item1);
        Assert.NotEqual(looks[2], looks[4]);
        foreach (var id in new[] { "bar-democrat-stripes", "bar-quiet-dots", "bar-quiet-grid" })
            Assert.NotNull(page.QuerySelector($"pattern#{id}"));
    }

    [Fact]
    public async Task ASegmentWithNoMembersIsInTheLegendAndIsNotDrawn()
    {
        var html = await RenderAsync(VotePageView.CastBar("Group", PartyTone.Neutral, 3, 0, 0, 1, 0, VoteThreshold.None), "bar");
        var page = await new HtmlParser().ParseDocumentAsync(html);

        Assert.Equal(2, page.QuerySelectorAll("svg.party-bar-chart rect.party-bar-fill").Length);
        Assert.Equal(3, page.QuerySelectorAll(".party-bar-legend li").Length);
        Assert.NotNull(page.QuerySelector("pattern#bar-neutral-stripes"));
    }

    [Fact]
    public async Task AnyCallerCanDrawPartySegmentsWithTheirOwnLabelsForAPartySplitAndTheLineIsOptional()
    {
        var split = new PartyBarData("Cosponsors", [new("Democrats", 12, PartyTone.Democrat, PartyBarFill.Solid), new("Republicans", 3, PartyTone.Republican, PartyBarFill.Stripes),
            new("Independents", 1, PartyTone.Neutral, PartyBarFill.Dots)]);
        var page = await new HtmlParser().ParseDocumentAsync(await RenderAsync(split, "split"));

        Assert.Equal(["democrat", "republican", "neutral"], page.QuerySelectorAll("svg.party-bar-chart rect.party-bar-fill").Select(rect => rect.GetAttribute("data-tone")));
        Assert.Equal(["12 Democrats", "3 Republicans", "1 Independents"], page.QuerySelectorAll(".party-bar-legend li").Select(item => Regex.Replace(item.TextContent, @"\s+", " ").Trim()));
        Assert.Null(page.QuerySelector("line"));
    }

    private static async Task<string> RenderAsync(PartyBarData data, string id)
    {
        await using var renderer = new HtmlRenderer(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<PartyBar>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(PartyBar.Id)] = id, [nameof(PartyBar.Data)] = data }));
            return output.ToHtmlString();
        });
    }
}
