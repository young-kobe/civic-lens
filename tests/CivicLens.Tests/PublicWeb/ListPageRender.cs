using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

/// <summary>Renders a list page component to a parsed document, as the server page renderer does.</summary>
internal static class ListPageRender
{
    public const string Root = "https://civic-lens.test/";

    public static async Task<IDocument> RenderAsync<TComponent>(object data, string dataName) where TComponent : IComponent
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [dataName] = data, ["RootPath"] = Root }));
            return output.ToHtmlString();
        });
        return await new HtmlParser().ParseDocumentAsync(html);
    }

    /// <summary>True when the first element comes before the second in the document.</summary>
    public static bool Precedes(IElement first, IElement second) =>
        first.CompareDocumentPosition(second).HasFlag(DocumentPositions.Following);
}
