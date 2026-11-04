using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.Search;

public static class SearchEndpoints
{
    public static void MapSearch(this IEndpointRouteBuilder routes)
    {
        var v1 = routes.MapGroup("/v1");
        v1.MapGet("/search", (HttpContext http, SearchIndex index, CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
        {
            var request = SearchRequest.Parse(http.Request.Query);
            if (await index.FindReleaseAsync(request.Release, cancellationToken) is not { } release) return NotIndexed(request.Release);
            return ApiResults.Json(await index.SearchAsync(release.ReleaseNumber, request, cancellationToken));
        }));
        v1.MapGet("/records/{id}", (string id, HttpContext http, SearchIndex index, CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
        {
            if (!PublicationProtocol.IsAnyRecordId(id)) return ApiResults.RecordNotFound();
            var requested = SearchRequest.ReleaseNumber(http.Request.Query);
            if (await index.FindReleaseAsync(requested, cancellationToken) is not { } release) return NotIndexed(requested);
            return await index.FindRecordAsync(id, release.ReleaseNumber, cancellationToken) is { } record
                ? ApiResults.Json(record)
                : ApiResults.RecordNotFound();
        }));
    }

    private static IResult NotIndexed(int? requested) => requested is null
        ? ApiResults.NoRelease()
        : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "release not indexed");
}
