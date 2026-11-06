using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.Home;

public static class HomeEndpoints
{
    public static void MapHome(this IEndpointRouteBuilder routes)
    {
        var v1 = routes.MapGroup("/v1");
        v1.MapGet("/home", (HomeStore home, CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
            await home.ReadAsync(cancellationToken) is { } answer ? ApiResults.Json(answer) : ApiResults.NoRelease()));
    }
}
