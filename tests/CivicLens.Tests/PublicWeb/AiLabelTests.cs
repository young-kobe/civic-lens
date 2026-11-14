using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Components.Ui;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class AiLabelTests
{
    [Theory]
    [InlineData(PublishedTextOrigin.Model, AiLabelLook.Tag, "ai-label ai-label-tag", "model")]
    [InlineData(PublishedTextOrigin.Code, AiLabelLook.Tag, "ai-label ai-label-tag", "code")]
    [InlineData(PublishedTextOrigin.Model, AiLabelLook.Line, "ai-label ai-label-line", "model")]
    [InlineData(PublishedTextOrigin.Code, AiLabelLook.Line, "ai-label ai-label-line", "code")]
    [InlineData(null, AiLabelLook.Line, "ai-label ai-label-line", null)]
    public async Task ALabelNamesItsLookAndTheOriginOfItsTextSoTheStylesheetMutesOnlyACodeLabel(PublishedTextOrigin? origin, AiLabelLook look, string classes, string? dataOrigin)
    {
        var label = Assert.Single((await RenderAsync(AiLabelWords.ModelWording, origin, look, "summary-origin")).QuerySelectorAll("body > *"));

        Assert.Equal("SPAN", label.TagName);
        Assert.Equal(classes, label.ClassName);
        Assert.Equal(dataOrigin, label.GetAttribute("data-origin"));
        Assert.Equal("summary-origin", label.Id);
        Assert.Equal(AiLabelWords.ModelWording, label.TextContent);
    }

    [Fact]
    public async Task AMissingLabelDrawsNothingSoACallerNeedsNoCheckOfItsOwn()
    {
        Assert.Empty((await RenderAsync(null, PublishedTextOrigin.Model, AiLabelLook.Tag, null)).Body!.Children);
    }

    [Fact]
    public void TheStylesheetMutesACodeLabelInBothLooksAndGivesEveryOtherLabelTheAiInk()
    {
        var css = WebStyleSheets.Read("public.css");

        Assert.Contains(".ai-label-line { color: var(--ai-ink);", css, StringComparison.Ordinal);
        Assert.Contains(".ai-label-line[data-origin=\"code\"] { color: var(--muted); }", css, StringComparison.Ordinal);
        Assert.Contains("background: var(--ai-line); color: var(--ai-label-ink);", css[css.IndexOf(".ai-label-tag {", StringComparison.Ordinal)..], StringComparison.Ordinal);
        Assert.Contains(".ai-label-tag[data-origin=\"code\"] {", css, StringComparison.Ordinal);
    }

    private static async Task<IDocument> RenderAsync(string? text, PublishedTextOrigin? origin, AiLabelLook look, string? id)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<AiLabel>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(AiLabel.Text)] = text,
                    [nameof(AiLabel.Origin)] = origin,
                    [nameof(AiLabel.Look)] = look,
                    [nameof(AiLabel.Id)] = id
                }));
            return output.ToHtmlString();
        });
        return new HtmlParser().ParseDocument(html);
    }
}
