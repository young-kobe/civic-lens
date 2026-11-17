using AngleSharp.Dom;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Publication;
using Microsoft.AspNetCore.Components;

namespace CivicLens.Tests.PublicWeb;

public sealed class PublishedCorrectionListTests
{
    internal static readonly PublishedCorrection[] History =
    [
        PublishedUpdateLineTests.SourceUpdate,
        PublishedCorrectionBannerTests.Newest,
        new()
        {
            ReplacedRevisionNumber = 1,
            PublishedAtUtc = new DateTimeOffset(2026, 11, 5, 10, 7, 0, TimeSpan.Zero),
            Note = "An older correction with <em>literal markup</em>."
        }
    ];

    internal static void AssertFullHistory(IDocument page)
    {
        var section = Assert.Single(page.QuerySelectorAll("[aria-labelledby=corrections-title]"));
        var heading = section.QuerySelector("h2")!;
        Assert.Equal(("corrections-title", "Corrections"), (heading.Id, heading.TextContent));
        var entries = section.QuerySelectorAll(".corrections > li");
        Assert.Equal(3, entries.Length);
        Assert.StartsWith("Updated from the source, 9 Nov 2026. Replaces revision 3.", entries[0].TextContent.Trim());
        Assert.Contains(PublishedCorrectionBannerTests.Newest.Note!, entries[1].TextContent);
        Assert.Contains(History[2].Note!, entries[2].TextContent);
        Assert.Null(entries[2].QuerySelector("em"));
    }

    internal static void AssertEmptyHistory(IDocument page)
    {
        Assert.Null(page.QuerySelector(".corrections"));
        Assert.Null(page.QuerySelector("#corrections-title"));
    }

    [Fact]
    public async Task TheSharedSectionRetainsTheWholeOrderedHistoryAndEncodesStoredNotes()
    {
        var page = await RenderAsync(History);

        AssertFullHistory(page);
    }

    [Fact]
    public async Task NoHistoryLeavesNoEmptySectionOrHeading()
    {
        AssertEmptyHistory(await RenderAsync([]));
    }

    private static Task<IDocument> RenderAsync(PublishedCorrection[] corrections) =>
        ListPageRender.RenderPageAsync<CascadingValue<GlossaryPageUse>>(new Dictionary<string, object?>
        {
            [nameof(CascadingValue<GlossaryPageUse>.Value)] = new GlossaryPageUse(),
            [nameof(CascadingValue<GlossaryPageUse>.IsFixed)] = true,
            [nameof(CascadingValue<GlossaryPageUse>.ChildContent)] = (RenderFragment)(builder =>
            {
                builder.OpenComponent<PublishedCorrectionList>(0);
                builder.AddAttribute(1, nameof(PublishedCorrectionList.Kind), PublishedRecordKind.Statement);
                builder.AddAttribute(2, nameof(PublishedCorrectionList.Corrections), corrections);
                builder.CloseComponent();
            })
        });
}
