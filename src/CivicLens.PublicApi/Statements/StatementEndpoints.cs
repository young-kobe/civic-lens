using CivicLens.PublicApi.Http;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.Statements;

public static class StatementEndpoints
{
    public static void MapStatements(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/v1");
        group.MapGet("/statements",
            (HttpContext http, StatementStore statements, CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
            {
                var request = StatementRequest.Parse(http.Request.Query);
                if (await statements.ListAsync(request, cancellationToken) is not { } page) return ApiResults.NoRelease();
                return request.Cursor is { } cursor && cursor.Release != page.Release
                    ? ApiResults.ReleaseChanged(page.Release)
                    : ApiResults.Json(page);
            }));
        group.MapGet("/statements/{recordId}", (string recordId, StatementStore statements, CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(async () =>
            {
                if (!PublicationProtocol.IsAnyRecordId(recordId)) return ApiResults.RecordNotFound();
                var result = await statements.FindAsync(recordId, cancellationToken);
                return result.Release is null ? ApiResults.NoRelease() : result.Detail is null ? ApiResults.RecordNotFound() : ApiResults.Json(result.Detail);
            }));
    }
}
