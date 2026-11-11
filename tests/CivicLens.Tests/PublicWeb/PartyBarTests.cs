using CivicLens.PublicWeb.Components.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class PartyBarTests
{
    [Theory]
    [InlineData(58, 152, 4, 0, 106)]
    [InlineData(3, 3, 0, 0, 4)]
    [InlineData(1, 0, 0, 0, 1)]
    public void TheMajorityLineIsTheSmallestYesCountAboveHalfOfTheYesAndNoCasts(int yes, int no, int notVoting, int other, int expected)
    {
        Assert.Equal(expected, new PartyBarData("Group", PartyTone.Neutral, yes, no, 0, notVoting, other).Majority);
    }

    [Fact]
    public void NobodyCastingYesOrNoGivesNoMajorityLine()
    {
        Assert.Null(new PartyBarData("Group", PartyTone.Neutral, 0, 0, 2, 3, 0).Majority);
    }

    [Fact]
    public void TheDescriptionNamesTheGroupAndEveryCount()
    {
        Assert.Equal("Group: Yes 1, No 2, Present 3, Not voting 4, Other 5", new PartyBarData("Group", PartyTone.Neutral, 1, 2, 3, 4, 5).Description);
    }

    [Fact]
    public async Task TheBarShowsNoWithAStripePatternAndNamesEveryCastInALegendSoColorIsNotTheOnlyCue()
    {
        var html = await RenderAsync(new PartyBarData("Democrats", PartyTone.Democrat, 3, 2, 1, 4, 5), "bar");

        Assert.Contains("<pattern id=\"bar-hatch\"", html, StringComparison.Ordinal);
        Assert.Contains("fill=\"url(#bar-hatch)\"", html, StringComparison.Ordinal);
        foreach (var word in new[] { "Yes", "No", "Present", "Other", "Not voting" })
            Assert.Contains("</b> " + word + "</li>", html, StringComparison.Ordinal);
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
