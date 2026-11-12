using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.Boards;

public static class BoardEndpoints
{
    public static void MapBoards(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/v1/boards", (BoardStore store, CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
            await store.FindAsync(cancellationToken) is { } answer
                ? ApiResults.Json(answer)
                : ApiResults.NoRelease()));
}
