using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Publication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class OfficialPortraitTests
{
    [Fact]
    public async Task CompactPathOnlyPortraitUsesTheRootAndOmitsUnknownMetadata()
    {
        var hash = new string('a', 64);

        var html = await RenderAsync(new Dictionary<string, object?>
        {
            [nameof(OfficialPortrait.Name)] = "A & B",
            [nameof(OfficialPortrait.PhotoPath)] = $"photos/{hash}.jpg",
            [nameof(OfficialPortrait.RootPath)] = "../",
            [nameof(OfficialPortrait.Compact)] = true
        });

        var document = new HtmlParser().ParseDocument(html);
        var image = document.QuerySelector("figure.portrait img")!;
        Assert.Equal($"../photos/{hash}.jpg", image.GetAttribute("src"));
        Assert.Equal("Official portrait of A & B", image.GetAttribute("alt"));
        Assert.Equal("lazy", image.GetAttribute("loading"));
        Assert.Null(image.GetAttribute("width"));
        Assert.Null(image.GetAttribute("height"));
        Assert.Null(document.QuerySelector("figcaption"));
        var fallback = Assert.IsAssignableFrom<IHtmlTemplateElement>(document.QuerySelector("template[data-portrait-fallback]"));
        Assert.NotNull(fallback.Content.QuerySelector("svg.portrait-placeholder"));
    }

    [Fact]
    public async Task MalformedPhotoPathRendersTheCompactPlaceholderInsteadOfAnImage()
    {
        var html = await RenderAsync(new Dictionary<string, object?>
        {
            [nameof(OfficialPortrait.Name)] = "A Official",
            [nameof(OfficialPortrait.PhotoPath)] = "https://outside.example/photo.jpg",
            [nameof(OfficialPortrait.RootPath)] = "",
            [nameof(OfficialPortrait.Compact)] = true
        });

        var figure = new HtmlParser().ParseDocument(html).QuerySelector("figure.portrait")!;
        Assert.Null(figure.QuerySelector("img"));
        Assert.NotNull(figure.QuerySelector("svg.portrait-placeholder"));
        Assert.Null(figure.QuerySelector("figcaption"));
        Assert.Null(figure.QuerySelector("template"));
    }

    private static async Task<string> RenderAsync(IDictionary<string, object?> parameters)
    {
        await using var renderer = new HtmlRenderer(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<OfficialPortrait>(ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        });
    }
}
