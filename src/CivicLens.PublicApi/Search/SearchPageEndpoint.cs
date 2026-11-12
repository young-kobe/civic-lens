using CivicLens.PublicApi.Http;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Pages;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Search;

/// <summary>The gated search page. The page holds the words that a reader typed, so it skips the shared page cache. It sends no validator and asks every cache not to keep it.</summary>
public static class SearchPageEndpoint
{
    public const string CacheControl = "private, no-store";

    public static Task<IResult> RespondAsync(HttpContext context, SearchPageReader reader, PageRenderer renderer, PublicBaseUrl baseUrl,
        CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
    {
        if (await reader.ReadAsync(context.Request.Query, cancellationToken) is not { } data) return ApiResults.NoRelease();
        var html = await renderer.RenderAsync<SearchResultsPage>(new Dictionary<string, object?>
        {
            [nameof(SearchResultsPage.Data)] = data,
            [nameof(SearchResultsPage.RootPath)] = baseUrl.Join(string.Empty)
        });
        context.Response.Headers.CacheControl = CacheControl;
        return Results.Content(html, "text/html; charset=utf-8");
    });
}
