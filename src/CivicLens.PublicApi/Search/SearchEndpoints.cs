using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Npgsql;

namespace CivicLens.PublicApi.Search;

public static class SearchEndpoints
{
    private const string StatementTimeoutState = "57014";
    private const int MaximumRecordIdLength = 64;

    public static void MapSearch(this IEndpointRouteBuilder routes)
    {
        var v1 = routes.MapGroup("/v1");
        v1.MapGet("/release", (SearchIndex index, CancellationToken cancellationToken) => GuardAsync(async () =>
            await index.FindReleaseAsync(null, cancellationToken) is { } release ? Json(release) : NoRelease()));
        v1.MapGet("/search", (HttpContext http, SearchIndex index, CancellationToken cancellationToken) => GuardAsync(async () =>
        {
            var request = SearchRequest.Parse(http.Request.Query);
            if (await index.FindReleaseAsync(request.Release, cancellationToken) is not { } release) return NotIndexed(request.Release);
            return Json(await index.SearchAsync(release.ReleaseNumber, request, cancellationToken));
        }));
        v1.MapGet("/records/{id}", (string id, HttpContext http, SearchIndex index, CancellationToken cancellationToken) => GuardAsync(async () =>
        {
            if (id.Length > MaximumRecordIdLength) return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "record not found");
            var requested = SearchRequest.ReleaseNumber(http.Request.Query);
            if (await index.FindReleaseAsync(requested, cancellationToken) is not { } release) return NotIndexed(requested);
            return await index.FindRecordAsync(id, release.ReleaseNumber, cancellationToken) is { } record
                ? Json(record)
                : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "record not found");
        }));
    }

    private static IResult Json<T>(T value) => Results.Json(value, PublicationProtocol.JsonOptions);

    private static IResult NotIndexed(int? requested) => requested is null
        ? NoRelease()
        : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "release not indexed");

    private static IResult NoRelease() =>
        Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "no release is indexed yet");

    private static async Task<IResult> GuardAsync(Func<Task<IResult>> read)
    {
        try
        {
            return await read();
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "invalid request", detail: exception.Message);
        }
        catch (PostgresException exception) when (exception.SqlState == StatementTimeoutState)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "the search took too long");
        }
    }
}
