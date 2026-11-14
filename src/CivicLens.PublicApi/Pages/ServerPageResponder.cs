using CivicLens.PublicApi.ActiveRelease;
using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Pages;

/// <summary>The pipeline of every server page: find the active release, answer 304 for a matching successful page, or render before setting cache headers.
/// A successful page gets the shared validator and freshness. Any other page, such as the not found page, gets no validator and no-store.
/// One read gives the release number and the freshness line that the shell of every page shows. A 304 skips the render, and a caller that already holds
/// its release number skips that read until it renders. A caller that already holds the freshness, such as Home, skips that read. One private core holds the 304 rule.</summary>
public sealed class ServerPageResponder(ActiveReleaseReader releases, PageCache cache, PageRenderer renderer)
{
    public async Task<IResult> RespondAsync<TComponent>(HttpContext context, int statusCode, IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken) where TComponent : IComponent
    {
        if (await releases.FindFreshnessAsync(cancellationToken) is not { } release) return ApiResults.NoRelease();
        return await RespondAsync<TComponent>(context, statusCode, release.ReleaseNumber, () => Task.FromResult(parameters), release, cancellationToken);
    }

    public Task<IResult> RespondAsync<TComponent>(HttpContext context, int statusCode, int releaseNumber,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken) where TComponent : IComponent =>
        RespondAsync<TComponent>(context, statusCode, releaseNumber, () => Task.FromResult(parameters), null, cancellationToken);

    /// <summary>For a page that already holds its freshness and whose costly reads belong after the 304 check. The parameters are read only when the page renders.</summary>
    public Task<IResult> RespondAsync<TComponent>(HttpContext context, int statusCode, ReleaseFreshness freshness,
        Func<Task<IReadOnlyDictionary<string, object?>>> parameters, CancellationToken cancellationToken) where TComponent : IComponent =>
        RespondAsync<TComponent>(context, statusCode, freshness.ReleaseNumber, parameters, freshness, cancellationToken);

    private async Task<IResult> RespondAsync<TComponent>(HttpContext context, int statusCode, int releaseNumber,
        Func<Task<IReadOnlyDictionary<string, object?>>> parameters, ReleaseFreshness? read, CancellationToken cancellationToken) where TComponent : IComponent
    {
        if (IsSuccess(statusCode) && cache.IsNotModified(context, releaseNumber))
        {
            cache.Apply(context, releaseNumber);
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }
        if ((read ?? await releases.FindFreshnessAsync(cancellationToken)) is not { } freshness) return ApiResults.NoRelease();
        var html = await renderer.RenderPageAsync<TComponent>(await parameters(), freshness.PageContextFor(releaseNumber));
        if (IsSuccess(statusCode)) cache.Apply(context, releaseNumber);
        else PageCache.ApplyFailure(context);
        return Results.Content(html, "text/html; charset=utf-8", statusCode: statusCode);
    }

    private static bool IsSuccess(int statusCode) => statusCode is >= 200 and < 300;
}
