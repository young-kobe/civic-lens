using CivicLens.PublicApi.ActiveRelease;
using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Pages;

/// <summary>The pipeline of every server page: find the active release, answer 304 for a matching successful page, or render before setting cache headers.
/// The release read comes first, because the ETag needs the release number. A 304 skips the render.</summary>
public sealed class ServerPageResponder(ActiveReleaseReader releases, PageCache cache, PageRenderer renderer)
{
    public async Task<IResult> RespondAsync<TComponent>(HttpContext context, int statusCode, IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken) where TComponent : IComponent
    {
        if (await releases.FindAsync(cancellationToken) is not { } release) return ApiResults.NoRelease();
        if (statusCode is >= 200 and < 300 && cache.IsNotModified(context, release.ReleaseNumber))
        {
            cache.Apply(context, release.ReleaseNumber);
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }
        var html = await renderer.RenderAsync<TComponent>(parameters);
        cache.Apply(context, release.ReleaseNumber);
        return Results.Content(html, "text/html; charset=utf-8", statusCode: statusCode);
    }
}
