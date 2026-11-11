using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class WithdrawalNoticePageTests
{
    private const string Root = "https://civic-lens.test/";

    public static readonly PublishedWithdrawal Notice = new()
    {
        Kind = PublishedRecordKind.Statement,
        RecordId = new string('a', 64),
        WithdrawnRevisionNumber = 4,
        FirstPublishedAtUtc = new(2026, 10, 30, 0, 44, 0, TimeSpan.Zero),
        WithdrawnAtUtc = new(2026, 11, 6, 12, 30, 0, TimeSpan.Zero),
        Note = "The quote came from a page that the office replaced. <i>Not</i> markup.",
        Corrections =
        [
            new() { ReplacedRevisionNumber = 3, PublishedAtUtc = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero), Note = "A person fixed the date." },
            new() { ReplacedRevisionNumber = 2, PublishedAtUtc = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero), FactsUnchanged = false },
            new() { ReplacedRevisionNumber = 1, PublishedAtUtc = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero) }
        ]
    };

    private static async Task<IDocument> RenderAsync(PublishedWithdrawal notice, string? root = Root)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = new Dictionary<string, object?> { ["Withdrawal"] = notice };
            if (root is not null) parameters["RootPath"] = root;
            return (await renderer.RenderComponentAsync<PublishedWithdrawalPage>(ParameterView.FromDictionary(parameters))).ToHtmlString();
        });
        return await new HtmlParser().ParseDocumentAsync(html);
    }

    [Fact]
    public async Task TheNoticeSaysWhatWasWithdrawnAndWhenWithTheExactRevisionAndDatesAndTheEscapedNote()
    {
        var page = await RenderAsync(Notice);

        Assert.Equal("This record was withdrawn", page.QuerySelector("h1")!.TextContent);
        var facts = page.QuerySelectorAll("dt").Zip(page.QuerySelectorAll("dd"), (name, value) => (name.TextContent.Trim(), value.TextContent.Trim())).ToArray();
        Assert.Equal(
        [
            ("Kind", "Statement"), ("Record", Notice.RecordId), ("Withdrawn revision", "4"),
            ("First published", "30 Oct 2026, 00:44 UTC"), ("Withdrawn", "6 Nov 2026, 12:30 UTC"), ("Note", Notice.Note)
        ], facts);
        Assert.Null(page.QuerySelector("dd b, dd i"));
    }

    [Fact]
    public async Task TheCorrectionHistoryShowsEveryEntryOfTheNoticeInTheReleaseOrderWithItsNoteOrItsSourceLabel()
    {
        var page = await RenderAsync(Notice);

        var entries = page.QuerySelectorAll(".corrections li").Select(item => item.TextContent.Replace("  ", " ").Trim()).ToArray();
        Assert.Equal(3, entries.Length);
        Assert.StartsWith("Correction", entries[0]);
        Assert.Contains(", 2 Nov 2026. Replaces revision 3.", entries[0]);
        Assert.Contains("A person fixed the date.", entries[0]);
        Assert.StartsWith("Updated from the source, 1 Nov 2026. Replaces revision 2.", entries[1]);
        Assert.StartsWith("Updated from the source, 31 Oct 2026. Replaces revision 1.", entries[2]);
    }

    [Fact]
    public async Task ANoticeWithNoCorrectionsShowsNoCorrectionList()
    {
        var page = await RenderAsync(Notice with { Corrections = [] });

        Assert.Null(page.QuerySelector(".corrections"));
    }

    [Fact]
    public async Task TheNoticeUsesTheConfiguredRootAndStaysOutOfSearchResults()
    {
        var page = await RenderAsync(Notice);

        Assert.StartsWith(Root, page.QuerySelector(".public-breadcrumbs a")!.GetAttribute("href"));
        Assert.Contains("noindex", page.QuerySelector("meta[name=robots]")!.GetAttribute("content"));
    }

    [Fact]
    public async Task TheStaticPageKeepsItsRelativeRootWhenNoRootIsGiven()
    {
        var page = await RenderAsync(Notice, root: null);

        Assert.StartsWith("../", page.QuerySelector(".public-breadcrumbs a")!.GetAttribute("href"));
    }
}
