using CivicLens.PublicWeb.Publication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace CivicLens.PublicApi.Pages;

/// <summary>Renders a public component to static HTML. There is no interactivity, no SignalR, and no WebAssembly.
/// A page renders inside the server page frame, which gives its shell the context. A fragment renders alone.</summary>
public sealed class PageRenderer(IServiceProvider services, ILoggerFactory loggerFactory)
{
    public Task<string> RenderAsync<TComponent>(IReadOnlyDictionary<string, object?> parameters)
        where TComponent : IComponent => RenderAsync<TComponent>(new Dictionary<string, object?>(parameters));

    public Task<string> RenderPageAsync<TComponent>(IReadOnlyDictionary<string, object?> parameters, ServerPageContext context)
        where TComponent : IComponent => RenderAsync<ServerPageFrame>(new Dictionary<string, object?>
        {
            [nameof(ServerPageFrame.Context)] = context,
            [nameof(ServerPageFrame.Page)] = typeof(TComponent),
            [nameof(ServerPageFrame.Parameters)] = parameters.ToDictionary(pair => pair.Key, pair => pair.Value!)
        });

    private async Task<string> RenderAsync<TComponent>(Dictionary<string, object?> parameters)
        where TComponent : IComponent
    {
        await using var renderer = new HtmlRenderer(services, loggerFactory);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        });
    }
}
