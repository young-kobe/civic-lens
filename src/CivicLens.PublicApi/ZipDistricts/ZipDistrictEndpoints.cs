using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Pages;
using CivicLens.PublicWeb.Publication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;

namespace CivicLens.PublicApi.ZipDistricts;

public static class ZipDistrictEndpoints
{
    /// <summary>The district file changes at most once a week and the release at most once an hour, so a shared cache may repeat an answer for an hour.</summary>
    private const string CacheControl = "public, max-age=3600";

    public static void MapZipDistricts(this IEndpointRouteBuilder routes)
    {
        var v1 = routes.MapGroup("/v1");
        v1.MapGet("/zip-districts/{zcta}", (string zcta, HttpContext http, ZipDistrictStore zips, CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
        {
            var request = ZipDistrictRequest.Parse(zcta, http.Request.Query);
            var lookup = await zips.FindAsync(request, cancellationToken);
            if (lookup.Answer is not { } answer) return Miss(lookup.Miss!.Value);
            http.Response.Headers[HeaderNames.CacheControl] = CacheControl;
            return ApiResults.Json(answer);
        }));
        v1.MapGet("/zip-districts/{zcta}/members", MembersAsync);
    }

    private static Task<IResult> MembersAsync(string zcta, HttpContext http, ZipDistrictStore zips, PageRenderer renderer, PublicApiOptions options,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        return ApiResults.GuardAsync(async () =>
        {
            var request = ZipDistrictRequest.Parse(zcta, http.Request.Query);
            var lookup = await zips.FindAsync(request, cancellationToken);
            if (lookup.Answer is not { } answer) return Miss(lookup.Miss!.Value);
            var html = await renderer.RenderAsync<ZipMemberCards>(new Dictionary<string, object?>
            {
                [nameof(ZipMemberCards.Data)] = ZipMemberCardsMapper.Map(answer),
                [nameof(ZipMemberCards.RootPath)] = options.PublicBaseUrl?.Join(string.Empty) ?? "/"
            });
            return Results.Content(html, "text/html; charset=utf-8");
        });
    }

    private static IResult Miss(ZipLookupMiss miss) => miss switch
    {
        ZipLookupMiss.NoRelease => ApiResults.NoRelease(),
        ZipLookupMiss.NoNames => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "the active release has no official names yet"),
        _ => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "zip area not found")
    };
}
