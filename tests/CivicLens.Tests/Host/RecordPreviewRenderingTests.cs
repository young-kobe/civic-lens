using CivicLens.Host.Pages.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CivicLens.Tests.Host;

public sealed class RecordPreviewRenderingTests
{
    [Fact]
    public async Task RendersStandaloneStaticPreviewAndEncodesUntrustedText()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await using var serviceProvider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(serviceProvider,
            serviceProvider.GetRequiredService<ILoggerFactory>());

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<RecordPreview>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(RecordPreview.RevisionNumber)] = 3,
                    [nameof(RecordPreview.Headline)] = "<script>alert('headline')</script>",
                    [nameof(RecordPreview.Summary)] = "Summary & evidence",
                    [nameof(RecordPreview.Institution)] = "Senate",
                    [nameof(RecordPreview.ChangeDate)] = "Not established",
                    [nameof(RecordPreview.Observed)] = "Before; after",
                    [nameof(RecordPreview.OfficialLinks)] = "None selected",
                    [nameof(RecordPreview.Issues)] = "None selected"
                }));
            return component.ToHtmlString();
        });

        Assert.Contains("<article class=\"review-preview\">", html, StringComparison.Ordinal);
        Assert.Contains("Proposed public record · Revision 3", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(&#x27;headline&#x27;)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Why it matters", html, StringComparison.Ordinal);
        Assert.DoesNotContain("What the evidence cannot establish", html, StringComparison.Ordinal);
    }
}
