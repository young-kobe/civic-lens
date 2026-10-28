using CivicLens.Host.Components.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.Host;

public sealed class FilterTabsTests
{
    [Fact]
    public async Task ATabListThatMixesLinksAndButtonsIsRefusedSoNoTabIsSilentlyTheWrongKind()
    {
        var tabs = new[] { FilterTab.Link("All", "?all", true), FilterTab.Button("Senators", "senator", false) };

        await Assert.ThrowsAsync<ArgumentException>(() => RenderAsync(tabs));
    }

    [Fact]
    public async Task LinkTabsRenderAsANavAndButtonTabsRenderAsAGroupOfButtons()
    {
        var links = await RenderAsync([FilterTab.Link("All", "?all", true, 3)]);
        var buttons = await RenderAsync([FilterTab.Button("All", "", true)]);

        Assert.Contains("<nav class=\"segmented\"", links, StringComparison.Ordinal);
        Assert.Contains("href=\"?all\"", links, StringComparison.Ordinal);
        Assert.Contains("<div class=\"segmented\" role=\"group\"", buttons, StringComparison.Ordinal);
        Assert.Contains("aria-pressed=\"true\"", buttons, StringComparison.Ordinal);
    }

    private static async Task<string> RenderAsync(IReadOnlyList<FilterTab> tabs)
    {
        await using var renderer = new HtmlRenderer(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<FilterTabs>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(FilterTabs.Label)] = "Filter", [nameof(FilterTabs.Tabs)] = tabs }));
            return output.ToHtmlString();
        });
    }
}
