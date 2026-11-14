using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Publication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

/// <summary>Renders a page component to a parsed document inside the server page frame, as the server page renderer does.</summary>
internal static class ListPageRender
{
    public const string Root = "https://civic-lens.test/";

    public static Task<IDocument> RenderAsync<TComponent>(object data, string dataName) where TComponent : IComponent =>
        RenderPageAsync<TComponent>(new Dictionary<string, object?> { [dataName] = data, ["RootPath"] = Root });

    public static async Task<IDocument> RenderPageAsync<TComponent>(IReadOnlyDictionary<string, object?> parameters, ServerPageContext? context = null) where TComponent : IComponent
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<ServerPageFrame>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ServerPageFrame.Context)] = context ?? new ServerPageContext(null, null),
                [nameof(ServerPageFrame.Page)] = typeof(TComponent),
                [nameof(ServerPageFrame.Parameters)] = parameters.ToDictionary(pair => pair.Key, pair => pair.Value!)
            }));
            return output.ToHtmlString();
        });
        return await new HtmlParser().ParseDocumentAsync(html);
    }

    /// <summary>True when the first element comes before the second in the document.</summary>
    public static bool Precedes(IElement first, IElement second) =>
        first.CompareDocumentPosition(second).HasFlag(DocumentPositions.Following);
}
