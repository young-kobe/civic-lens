using CivicLens.PublicWeb.Components.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class EmptyStateTests
{
    [Fact]
    public async Task AnEmptyStateWithNoChildContentKeepsItsTitleTextAndAction()
    {
        var html = await RenderAsync(new Dictionary<string, object?>
        {
            [nameof(EmptyState.Title)] = "Nothing here",
            [nameof(EmptyState.Text)] = "Try again later.",
            [nameof(EmptyState.Action)] = (RenderFragment)(builder => builder.AddMarkupContent(0, "<a href=\"/go\">Go</a>"))
        });

        Assert.Equal("<div class=\"empty\"><h3>Nothing here</h3><p>Try again later.</p><a href=\"/go\">Go</a></div>", html.Replace("\n", "").Replace("    ", ""));
    }

    [Fact]
    public async Task ChildContentRendersInsideTheEmptyStateAfterTheTextAndBeforeTheAction()
    {
        var html = await RenderAsync(new Dictionary<string, object?>
        {
            [nameof(EmptyState.Title)] = "Nothing here",
            [nameof(EmptyState.Text)] = "Try one of these.",
            [nameof(EmptyState.ChildContent)] = (RenderFragment)(builder => builder.AddMarkupContent(0, "<ul><li>Step</li></ul>")),
            [nameof(EmptyState.Action)] = (RenderFragment)(builder => builder.AddMarkupContent(0, "<a href=\"/go\">Go</a>"))
        });

        var text = html.IndexOf("Try one of these.", StringComparison.Ordinal);
        var list = html.IndexOf("<ul><li>Step</li></ul>", StringComparison.Ordinal);
        var action = html.IndexOf("<a href=\"/go\">Go</a>", StringComparison.Ordinal);
        Assert.InRange(list, text + 1, action - 1);
        Assert.StartsWith("<div class=\"empty\">", html, StringComparison.Ordinal);
        Assert.EndsWith("</div>", html, StringComparison.Ordinal);
    }

    private static async Task<string> RenderAsync(Dictionary<string, object?> parameters)
    {
        await using var renderer = new HtmlRenderer(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<EmptyState>(ParameterView.FromDictionary(parameters))).ToHtmlString());
    }
}
