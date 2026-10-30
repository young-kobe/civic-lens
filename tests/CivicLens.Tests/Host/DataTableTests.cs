using AngleSharp.Html.Parser;
using CivicLens.Host.Components.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.Host;

public sealed class DataTableTests
{
    [Fact]
    public async Task ATableThatAsksForNoSortRendersNoSortAttributeSoTheReviewTablesStayAsTheyWere()
    {
        var html = await RenderAsync(sortable: false);

        Assert.DoesNotContain("data-sort", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-default-order", html, StringComparison.Ordinal);
        var document = new HtmlParser().ParseDocument(html);
        Assert.Equal("Words", document.QuerySelector("caption")!.TextContent);
    }

    [Fact]
    public async Task ASortableTableMarksItsHeadersTheValueBehindACellAndTheDefaultOrderInTheCaption()
    {
        var document = new HtmlParser().ParseDocument(await RenderAsync(sortable: true));

        Assert.Equal("Words. Newest first.", document.QuerySelector("caption")!.TextContent);
        Assert.Equal("Newest first", document.QuerySelector("table")!.GetAttribute("data-default-order"));
        Assert.Equal(["text", "number"], document.QuerySelectorAll("th").Select(header => header.GetAttribute("data-sort")));
        Assert.Equal("7", document.QuerySelector("td[data-sort-value]")!.GetAttribute("data-sort-value"));
    }

    private static async Task<string> RenderAsync(bool sortable)
    {
        await using var renderer = new HtmlRenderer(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            RenderFragment head = builder =>
            {
                Cell(builder, header: true, sortable ? SortKind.Text : SortKind.None, null, "Word");
                Cell(builder, header: true, sortable ? SortKind.Number : SortKind.None, null, "Count");
            };
            RenderFragment<string> row = word => builder =>
            {
                Cell(builder, header: false, SortKind.None, null, word);
                Cell(builder, header: false, SortKind.None, sortable ? "7" : null, "7");
            };
            var parameters = new Dictionary<string, object?>
            {
                [nameof(DataTable<string>.Items)] = new[] { "a", "b" },
                [nameof(DataTable<string>.Head)] = head,
                [nameof(DataTable<string>.Row)] = row,
                [nameof(DataTable<string>.Caption)] = "Words",
                [nameof(DataTable<string>.DefaultOrder)] = sortable ? "Newest first" : null
            };
            return (await renderer.RenderComponentAsync<DataTable<string>>(ParameterView.FromDictionary(parameters))).ToHtmlString();
        });
    }

    private static void Cell(RenderTreeBuilder builder, bool header, SortKind sort, string? sortValue, string text)
    {
        builder.OpenComponent<TableCell>(0);
        builder.AddComponentParameter(1, nameof(TableCell.Header), header);
        builder.AddComponentParameter(2, nameof(TableCell.Sort), sort);
        builder.AddComponentParameter(3, nameof(TableCell.SortValue), sortValue);
        builder.AddComponentParameter(4, nameof(TableCell.ChildContent), (RenderFragment)(content => content.AddContent(0, text)));
        builder.CloseComponent();
    }
}
