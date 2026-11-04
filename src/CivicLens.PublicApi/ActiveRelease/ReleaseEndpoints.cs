using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.ActiveRelease;

public static class ReleaseEndpoints
{
    public static void MapRelease(this IEndpointRouteBuilder routes) =>
        routes.MapGroup("/v1").MapGet("/release", (ActiveReleaseReader reader, CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
            await reader.FindAsync(cancellationToken) is { } release ? ApiResults.Json(release) : ApiResults.NoRelease()));
}
