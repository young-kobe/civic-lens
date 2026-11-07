using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class HomePageTests
{
    private const string Root = "https://civic-lens.test/";
    private static readonly HomeFeaturedVoteData Featured = new(new string('a', 64), new(2026, 3, 3),
        PublishedChamber.House, "Published headline", null, "Published summary", PublishedTextOrigin.Model,
        "Passed", 216, 214, "Current bill line.", PublishedWording.BillLineSource);

    [Fact]
    public async Task UnknownTotalsStayAbsentButThePublishedResultRemainsVisible()
    {
        var page = await RenderAsync(new(7, Featured with { Yea = null, Nay = null }, [], null));

        Assert.Null(page.QuerySelector(".home-featured .home-vote-counts"));
        Assert.Equal("Passed", page.QuerySelector(".home-featured-result")!.TextContent);
        Assert.Contains("Current bill line. " + PublishedWording.BillLineSource, page.QuerySelector(".home-featured")!.TextContent);
    }

    [Theory]
    [InlineData(PublishedTextOrigin.Model, PublishedRecordLabels.ModelWording)]
    [InlineData(PublishedTextOrigin.Code, PublishedRecordLabels.CodeWording)]
    [InlineData(PublishedTextOrigin.Person, null)]
    public async Task TheSummaryKeepsItsPublishedOriginAndEscapesStoredText(PublishedTextOrigin origin, string? wording)
    {
        const string text = "<script>private()</script>";
        var page = await RenderAsync(new(7, Featured with { TextOrigin = origin, Summary = text }, [], null));
        var summary = page.QuerySelector(".home-featured-summary")!;

        Assert.Null(summary.QuerySelector("script"));
        Assert.Equal(text, summary.QuerySelector("p")!.TextContent);
        Assert.Equal(wording, summary.QuerySelector("span")?.TextContent);
    }

    [Fact]
    public async Task EmptyOptionalSectionsAreOmittedAndABriefIsOneLinkedLine()
    {
        var empty = await RenderAsync(new(7, null, [], null));
        Assert.Null(empty.QuerySelector(".home-featured"));
        Assert.Null(empty.QuerySelector(".home-preview-section"));
        Assert.Null(empty.QuerySelector(".home-brief-line"));

        var id = new string('b', 64);
        var brief = await RenderAsync(new(7, null, [], new(id, "Published brief", null)));
        var line = brief.QuerySelector(".home-brief-line")!;
        Assert.Contains("This week:", line.TextContent);
        Assert.Equal(Root + PublicationProtocol.RecordPagePath(id), line.QuerySelector("a")!.GetAttribute("href"));
    }

    private static async Task<IDocument> RenderAsync(HomePageData data)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<HomePage>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(HomePage.Data)] = data, [nameof(HomePage.RootPath)] = Root }));
            return output.ToHtmlString();
        });
        return await new HtmlParser().ParseDocumentAsync(html);
    }
}
