using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace CivicLens.PublicApi.Pages;

/// <summary>Renders a public component to static HTML. There is no interactivity, no SignalR, and no WebAssembly.</summary>
public sealed class PageRenderer(IServiceProvider services, ILoggerFactory loggerFactory)
{
    public async Task<string> RenderAsync<TComponent>(IReadOnlyDictionary<string, object?> parameters)
        where TComponent : IComponent
    {
        await using var renderer = new HtmlRenderer(services, loggerFactory);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(new Dictionary<string, object?>(parameters)));
            return output.ToHtmlString();
        });
    }
}
