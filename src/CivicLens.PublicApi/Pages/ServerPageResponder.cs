using CivicLens.PublicApi.ActiveRelease;
using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Pages;

/// <summary>The pipeline of every server page: find the active release, answer 304 for a matching successful page, or render before setting cache headers.
/// One read gives the release number and the freshness line that the shell of every page shows. A 304 skips the render, and a caller that already holds
/// its release number skips that read until it renders. A caller that already holds the freshness, such as Home, skips that read. One private method holds the 304 rule.</summary>
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
        if (statusCode is >= 200 and < 300 && NotModified(context, releaseNumber) is { } notModified) return notModified;
        if ((read ?? await releases.FindFreshnessAsync(cancellationToken)) is not { } freshness) return ApiResults.NoRelease();
        var html = await renderer.RenderPageAsync<TComponent>(await parameters(), freshness.PageContextFor(releaseNumber));
        cache.Apply(context, releaseNumber);
        return Results.Content(html, "text/html; charset=utf-8", statusCode: statusCode);
    }

    /// <summary>For a successful server file that is not a page, such as the sitemap. It has no shell, so it needs no freshness read. The content is built only when the file is sent.</summary>
    public IResult RespondFile(HttpContext context, int releaseNumber, Func<string> content, string contentType)
    {
        if (NotModified(context, releaseNumber) is { } notModified) return notModified;
        var text = content();
        cache.Apply(context, releaseNumber);
        return Results.Content(text, contentType);
    }

    private IResult? NotModified(HttpContext context, int releaseNumber)
    {
        if (!cache.IsNotModified(context, releaseNumber)) return null;
        cache.Apply(context, releaseNumber);
        return Results.StatusCode(StatusCodes.Status304NotModified);
    }
}
