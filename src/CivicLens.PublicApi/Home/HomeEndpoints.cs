using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;

namespace CivicLens.PublicApi.Home;

public static class HomeEndpoints
{
    /// <summary>The release changes at most once an hour, as for the ZIP route, and the query counts the member rows of every vote, so a shared cache may repeat an answer for an hour.</summary>
    private const string CacheControl = "public, max-age=3600";

    public static void MapHome(this IEndpointRouteBuilder routes)
    {
        var v1 = routes.MapGroup("/v1");
        v1.MapGet("/home", (HttpContext http, HomeStore home, CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
        {
            if (await home.ReadAsync(cancellationToken) is not { } answer) return ApiResults.NoRelease();
            http.Response.Headers[HeaderNames.CacheControl] = CacheControl;
            return ApiResults.Json(answer);
        }));
    }
}
