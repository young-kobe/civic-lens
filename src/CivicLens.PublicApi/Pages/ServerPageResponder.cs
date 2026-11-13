using CivicLens.PublicApi.ActiveRelease;
using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Pages;

/// <summary>The pipeline of every server page: find the active release, answer 304 for a matching successful page, or render before setting cache headers.
/// One read gives the release number and the freshness line that the shell of every page shows. A 304 skips the render, and a caller that already holds
/// its release number skips that read until it renders. A caller that already holds the freshness, such as Home, skips that read.</summary>
public sealed class ServerPageResponder(ActiveReleaseReader releases, PageCache cache, PageRenderer renderer)
{
    public async Task<IResult> RespondAsync<TComponent>(HttpContext context, int statusCode, IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken) where TComponent : IComponent
    {
        if (await releases.FindFreshnessAsync(cancellationToken) is not { } release) return ApiResults.NoRelease();
        return await RespondAsync<TComponent>(context, statusCode, release, parameters, cancellationToken);
    }

    public Task<IResult> RespondAsync<TComponent>(HttpContext context, int statusCode, int releaseNumber,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken) where TComponent : IComponent =>
        RespondAsync<TComponent>(context, statusCode, releaseNumber, parameters, null, cancellationToken);

    public Task<IResult> RespondAsync<TComponent>(HttpContext context, int statusCode, ReleaseFreshness freshness,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken) where TComponent : IComponent =>
        RespondAsync<TComponent>(context, statusCode, freshness.ReleaseNumber, parameters, freshness, cancellationToken);

    private async Task<IResult> RespondAsync<TComponent>(HttpContext context, int statusCode, int releaseNumber,
        IReadOnlyDictionary<string, object?> parameters, ReleaseFreshness? read, CancellationToken cancellationToken) where TComponent : IComponent
    {
        if (statusCode is >= 200 and < 300 && cache.IsNotModified(context, releaseNumber))
        {
            cache.Apply(context, releaseNumber);
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }
        if ((read ?? await releases.FindFreshnessAsync(cancellationToken)) is not { } freshness) return ApiResults.NoRelease();
        var html = await renderer.RenderPageAsync<TComponent>(parameters, freshness.PageContext);
        cache.Apply(context, releaseNumber);
        return Results.Content(html, "text/html; charset=utf-8", statusCode: statusCode);
    }
}
