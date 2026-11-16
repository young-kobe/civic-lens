using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Components.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class DataTableTests
{
    [Fact]
    public async Task TheVisuallyHiddenCaptionNamesTheTableWithTheGivenTextOnly()
    {
        var document = new HtmlParser().ParseDocument(await RenderAsync());

        Assert.Equal("Words", document.QuerySelector("caption.visually-hidden")!.TextContent);
        Assert.Equal(["a", "b"], document.QuerySelectorAll("tbody tr > td:first-child").Select(cell => cell.TextContent));
    }

    private static async Task<string> RenderAsync()
    {
        await using var renderer = new HtmlRenderer(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            RenderFragment head = builder =>
            {
                Cell(builder, header: true, "Word");
                Cell(builder, header: true, "Count");
            };
            RenderFragment<string> row = word => builder =>
            {
                Cell(builder, header: false, word);
                Cell(builder, header: false, "7");
            };
            var parameters = new Dictionary<string, object?>
            {
                [nameof(DataTable<string>.Items)] = new[] { "a", "b" },
                [nameof(DataTable<string>.Head)] = head,
                [nameof(DataTable<string>.Row)] = row,
                [nameof(DataTable<string>.Caption)] = "Words"
            };
            return (await renderer.RenderComponentAsync<DataTable<string>>(ParameterView.FromDictionary(parameters))).ToHtmlString();
        });
    }

    private static void Cell(RenderTreeBuilder builder, bool header, string text)
    {
        builder.OpenComponent<TableCell>(0);
        builder.AddComponentParameter(1, nameof(TableCell.Header), header);
        builder.AddComponentParameter(2, nameof(TableCell.ChildContent), (RenderFragment)(content => content.AddContent(0, text)));
        builder.CloseComponent();
    }
}
