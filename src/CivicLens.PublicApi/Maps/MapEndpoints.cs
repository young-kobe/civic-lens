using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.Maps;

public static class MapEndpoints
{
    public static void MapMaps(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/v1/maps", (HttpContext context, MapStore store, CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
            await store.FindAsync(MapRequest.Parse(context.Request.Query), cancellationToken) is { } answer
                ? ApiResults.Json(answer)
                : ApiResults.NoRelease()));
}
