using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class PublishedUpdateLineTests
{
    internal static readonly PublishedCorrection SourceUpdate = new()
    {
        ReplacedRevisionNumber = 3,
        PublishedAtUtc = new DateTimeOffset(2026, 11, 8, 23, 30, 0, TimeSpan.FromHours(-5))
    };

    private static readonly PublishedCorrection OlderSourceUpdate = new()
    {
        ReplacedRevisionNumber = 1,
        PublishedAtUtc = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero)
    };

    private static async Task<IDocument> RenderAsync(params PublishedCorrection[] corrections)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<PublishedUpdateLine>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(PublishedUpdateLine.Corrections)] = corrections
            }));
            return output.ToHtmlString();
        });
        return await new HtmlParser().ParseDocumentAsync(html);
    }

    [Fact]
    public async Task ARoutineUpdateFromTheSourceShowsTheApprovedHistoryLabelWithTheUtcDayOfTheNewestUpdateAsAMutedLine()
    {
        var line = (await RenderAsync(SourceUpdate, OlderSourceUpdate)).QuerySelector("p.update-line")!;

        Assert.Equal("Updated from the source, 9 Nov 2026.", line.TextContent);
        Assert.Equal(["small", "muted", "update-line"], line.ClassList);
    }

    [Fact]
    public async Task APersonCorrectionShowsTheBannerAndNoLineAlsoWhenANewerUpdateFromTheSourceFollowsIt()
    {
        Assert.Null((await RenderAsync(SourceUpdate, PublishedCorrectionBannerTests.Newest)).QuerySelector(".update-line"));
        Assert.Null((await RenderAsync(PublishedCorrectionBannerTests.Newest, OlderSourceUpdate)).QuerySelector(".update-line"));
    }

    [Fact]
    public async Task ASummaryRewriteOfAVoteIsNotAnUpdateFromTheSourceSoOnlyAnOlderSourceUpdateShows()
    {
        var rewrite = SourceUpdate with { FactsUnchanged = true };

        Assert.Null((await RenderAsync(rewrite)).QuerySelector(".update-line"));
        Assert.Equal("Updated from the source, 2 Nov 2026.", (await RenderAsync(rewrite, OlderSourceUpdate)).QuerySelector(".update-line")!.TextContent);
    }

    [Fact]
    public async Task ARecordWithNoHistoryShowsNoLine()
    {
        Assert.Null((await RenderAsync()).QuerySelector(".update-line"));
    }

    /// <summary>A record page shows the line in its record header, after the headline and before the main content, and never beside the banner.</summary>
    internal static void AssertTheLineIsInTheRecordHeader(IDocument page)
    {
        var line = page.QuerySelector("header .update-line")!;
        Assert.Equal("Updated from the source, 9 Nov 2026.", line.TextContent);
        Assert.Null(page.QuerySelector(".correction-banner"));
    }
}
