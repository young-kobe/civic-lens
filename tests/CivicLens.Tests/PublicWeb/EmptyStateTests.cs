using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Components.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class EmptyStateTests
{
    private static RenderFragment Fragment(string html) => builder => builder.AddMarkupContent(0, html);

    private static async Task<AngleSharp.Dom.IDocument> RenderAsync(RenderFragment? content, RenderFragment? action, int? level = null)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var parameters = new Dictionary<string, object?>
        {
            [nameof(EmptyState.Title)] = "Nothing here",
            [nameof(EmptyState.Text)] = "Try again.",
            [nameof(EmptyState.ChildContent)] = content,
            [nameof(EmptyState.Action)] = action
        };
        if (level is { } value) parameters[nameof(EmptyState.Level)] = value;
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<EmptyState>(ParameterView.FromDictionary(parameters))).ToHtmlString());
        return await new HtmlParser().ParseDocumentAsync(html);
    }

    [Fact]
    public async Task TheContentSlotShowsAfterTheTitleAndTextAndBeforeTheAction()
    {
        var page = await RenderAsync(Fragment("<ul class=\"steps\"><li>One</li></ul>"), Fragment("<a class=\"go\" href=\"#\">Go</a>"));

        var empty = page.QuerySelector(".empty")!;
        Assert.Equal(["H3", "P", "UL", "A"], empty.Children.Select(child => child.TagName));
        Assert.Equal("One", empty.QuerySelector(".steps li")!.TextContent);
    }

    [Fact]
    public async Task WithNoContentOrActionOnlyTheTitleAndTextShow()
    {
        var page = await RenderAsync(null, null);

        Assert.Equal(["H3", "P"], page.QuerySelector(".empty")!.Children.Select(child => child.TagName));
    }

    [Fact]
    public async Task APageThatShowsTheEmptyStateUnderItsTitleGetsALevelTwoHeadingSoNoLevelIsSkipped()
    {
        var page = await RenderAsync(null, null, level: 2);

        Assert.Equal(["H2", "P"], page.QuerySelector(".empty")!.Children.Select(child => child.TagName));
    }
}
