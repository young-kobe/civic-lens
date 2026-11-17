using CivicLens.PublicApi.ActiveRelease;
using CivicLens.PublicApi.Http;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Pages;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Search;

/// <summary>The search page. The page holds the words that a reader typed, so it skips the shared page cache. It sends no validator and asks every cache not to keep it.</summary>
public static class SearchPageEndpoint
{
    public const string CacheControl = "private, no-store";

    public static Task<IResult> RespondAsync(HttpContext context, SearchPageReader reader, ActiveReleaseReader releases, PageRenderer renderer, PublicBaseUrl baseUrl,
        CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
    {
        var (data, statusCode) = await ReadAsync(context.Request.Query, reader, cancellationToken);
        if (data is null) return ApiResults.NoRelease();
        if (await releases.FindFreshnessAsync(cancellationToken) is not { } freshness) return ApiResults.NoRelease();
        var html = await renderer.RenderPageAsync<SearchResultsPage>(new Dictionary<string, object?>
        {
            [nameof(SearchResultsPage.Data)] = data,
            [nameof(SearchResultsPage.RootPath)] = baseUrl.Join(string.Empty)
        }, freshness.PageContextFor(data.Release));
        context.Response.Headers.CacheControl = CacheControl;
        return Results.Content(html, "text/html; charset=utf-8", statusCode: statusCode);
    });

    private static async Task<(SearchPageData? Data, int StatusCode)> ReadAsync(IQueryCollection query, SearchPageReader reader, CancellationToken cancellationToken)
    {
        SearchRequest request;
        try
        {
            request = SearchRequest.ParsePage(query);
        }
        catch (ArgumentException refused)
        {
            return (await reader.ReadRefusedAsync(query, refused.ParamName, cancellationToken), StatusCodes.Status400BadRequest);
        }
        return (await reader.ReadAsync(request, cancellationToken), StatusCodes.Status200OK);
    }
}
