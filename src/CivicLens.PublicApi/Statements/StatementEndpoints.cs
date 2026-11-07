using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.Statements;

public static class StatementEndpoints
{
    public static void MapStatements(this IEndpointRouteBuilder routes) => routes.MapGroup("/v1").MapGet("/statements",
        (HttpContext http, StatementStore statements, CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
        {
            var request = StatementRequest.Parse(http.Request.Query);
            if (await statements.ListAsync(request, cancellationToken) is not { } page) return ApiResults.NoRelease();
            return request.Cursor is { } cursor && cursor.Release != page.Release ? ApiResults.ReleaseChanged(page.Release) : ApiResults.Json(page);
        }));
}
