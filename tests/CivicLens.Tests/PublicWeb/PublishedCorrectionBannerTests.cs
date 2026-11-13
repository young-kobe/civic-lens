using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class PublishedCorrectionBannerTests
{
    internal static readonly PublishedCorrection Newest = new()
    {
        ReplacedRevisionNumber = 2,
        PublishedAtUtc = new DateTimeOffset(2026, 11, 6, 7, 30, 0, TimeSpan.FromHours(-5)),
        Note = "The House Clerk changed one cast from Not voting to Yes."
    };

    private static readonly PublishedCorrection Older = new()
    {
        ReplacedRevisionNumber = 1,
        PublishedAtUtc = new DateTimeOffset(2026, 11, 5, 10, 7, 0, TimeSpan.Zero),
        Note = "An older note that the banner does not show."
    };

    private static async Task<IDocument> RenderAsync(PublishedRecordKind kind, params PublishedCorrection[] corrections)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<PublishedCorrectionBanner>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(PublishedCorrectionBanner.Kind)] = kind,
                [nameof(PublishedCorrectionBanner.Corrections)] = corrections
            }));
            return output.ToHtmlString();
        });
        return await new HtmlParser().ParseDocumentAsync(html);
    }

    /// <summary>A record page shows the banner right after its headline, so the banner heading keeps the heading order of the page.</summary>
    internal static void AssertTheBannerFollowsTheHeadline(IDocument page)
    {
        var headings = page.QuerySelectorAll("h1, h2, h3").ToList();
        var banner = page.QuerySelector(".correction-banner h2")!;
        Assert.Equal("H1", headings[headings.IndexOf(banner) - 1].TagName);
    }

    [Theory]
    [InlineData(PublishedRecordKind.Vote, "vote")]
    [InlineData(PublishedRecordKind.Action, "bill or resolution")]
    [InlineData(PublishedRecordKind.Statement, "statement")]
    [InlineData(PublishedRecordKind.ExecutiveAction, "executive action")]
    [InlineData(PublishedRecordKind.DocumentChange, "document change")]
    [InlineData(PublishedRecordKind.Brief, "weekly brief")]
    public async Task TheLineNamesTheKindWithTheWordsOfTheWithdrawalNoticeAndTheUtcTimeOfTheNewestCorrection(PublishedRecordKind kind, string words)
    {
        var page = await RenderAsync(kind, Newest, Older);

        Assert.Equal($"Civic Lens corrected this {words} on 6 Nov 2026, 12:30 UTC.", page.QuerySelector(".correction-banner p")!.TextContent);
    }

    [Fact]
    public async Task TheBannerIsAStatusWithTheApprovedTitleAsASecondLevelHeadingThatNamesIt()
    {
        var banner = (await RenderAsync(PublishedRecordKind.Vote, Newest)).QuerySelector(".correction-banner")!;
        var heading = banner.QuerySelector("h2")!;

        Assert.Equal("status", banner.GetAttribute("role"));
        Assert.Equal("Corrected record", heading.TextContent);
        Assert.Equal(heading.Id, banner.GetAttribute("aria-labelledby"));
    }

    [Fact]
    public async Task AStoredNoteOfTheNewestCorrectionShowsUnderWhatChanged()
    {
        var banner = (await RenderAsync(PublishedRecordKind.Vote, Newest, Older)).QuerySelector(".correction-banner")!;

        Assert.Equal(["What changed"], banner.QuerySelectorAll("dt").Select(term => term.TextContent));
        Assert.Equal([Newest.Note], banner.QuerySelectorAll("dd").Select(value => value.TextContent.Trim()));
    }

    [Fact]
    public async Task ACorrectionWithNoStoredNoteGivesNoWhatChangedText()
    {
        var banner = (await RenderAsync(PublishedRecordKind.Vote, Newest with { Note = null }, Older)).QuerySelector(".correction-banner")!;

        Assert.Null(banner.QuerySelector("dl"));
        Assert.DoesNotContain("What changed", banner.TextContent);
        Assert.DoesNotContain(Older.Note!, banner.TextContent);
    }

    [Fact]
    public async Task ARecordWithNoCorrectionShowsNoBanner()
    {
        Assert.Null((await RenderAsync(PublishedRecordKind.Vote)).QuerySelector(".correction-banner"));
    }
}
