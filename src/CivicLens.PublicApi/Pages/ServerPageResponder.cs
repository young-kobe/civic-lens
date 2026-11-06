using CivicLens.PublicApi.ActiveRelease;
using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Pages;

/// <summary>The pipeline of every server page: find the active release, set the cache headers, answer 304 when the reader holds the page, and render otherwise.
/// The release read comes first, because the ETag needs the release number. A 304 skips the render.</summary>
public sealed class ServerPageResponder(ActiveReleaseReader releases, PageCache cache, PageRenderer renderer)
{
    public async Task<IResult> RespondAsync<TComponent>(HttpContext context, int statusCode, IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken) where TComponent : IComponent
    {
        if (await releases.FindAsync(cancellationToken) is not { } release) return ApiResults.NoRelease();
        if (cache.Apply(context, release.ReleaseNumber)) return Results.StatusCode(StatusCodes.Status304NotModified);
        var html = await renderer.RenderAsync<TComponent>(parameters);
        return Results.Content(html, "text/html; charset=utf-8", statusCode: statusCode);
    }
}
