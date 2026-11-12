using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Bills;
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
        v1.MapGet("/zip-districts/{zcta}/bills/{recordId}", BillMembersAsync);
    }

    private static Task<IResult> MembersAsync(string zcta, HttpContext http, ZipDistrictStore zips, PageRenderer renderer, PublicApiOptions options,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        return ApiResults.GuardAsync(async () =>
        {
            var request = ZipDistrictRequest.Parse(zcta, http.Request.Query);
            var issue = QueryValues.Filter(http.Request.Query, "issue");
            var view = QueryValues.Value(http.Request.Query, "view") ?? string.Empty;
            if (view is not "" and not "compare" and not "strip")
                throw new ArgumentException("view must be empty, compare, or strip.", "view");
            if (view == "strip" && issue is not null)
                throw new ArgumentException("The strip view has no issue filter.", "issue");
            var sinceRelease = QueryValues.Number(http.Request.Query, "sinceRelease", 1, int.MaxValue);
            var lookup = view == "strip"
                ? await zips.FindStripAsync(request, sinceRelease, cancellationToken)
                : await zips.FindMembersAsync(request, issue, sinceRelease, cancellationToken);
            if (lookup.Answer is not { } answer) return Miss(lookup.Miss!.Value);
            var html = await renderer.RenderAsync<ZipMemberCards>(new Dictionary<string, object?>
            {
                [nameof(ZipMemberCards.Data)] = ZipMemberCardsMapper.Map(answer, lookup.SharedVotes, lookup.IssueOptions, issue),
                [nameof(ZipMemberCards.RootPath)] = options.PublicBaseUrl?.Join(string.Empty) ?? "/",
                [nameof(ZipMemberCards.View)] = view,
                [nameof(ZipMemberCards.SinceRelease)] = sinceRelease,
                [nameof(ZipMemberCards.NewVoteCount)] = lookup.NewVoteCount,
                [nameof(ZipMemberCards.ComparePageUrl)] = view != string.Empty || options.BrowserApiUrl is null
                    ? null
                    : options.PublicBaseUrl?.Join("compare.html")
            });
            return Results.Content(html, "text/html; charset=utf-8");
        });
    }

    /// <summary>The private fragment of the bill page card "Your members and this bill". Like the member cards, it names the members of one ZIP area, so no cache keeps it.</summary>
    private static Task<IResult> BillMembersAsync(string zcta, string recordId, HttpContext http, BillMembersReader reader, PageRenderer renderer, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        return ApiResults.GuardAsync(async () =>
        {
            var request = ZipDistrictRequest.Parse(zcta, http.Request.Query);
            if (!PublicationProtocol.IsAnyRecordId(recordId)) return ApiResults.RecordNotFound();
            var answer = await reader.ReadAsync(request, recordId, cancellationToken);
            if (answer.Miss is { } miss) return Miss(miss);
            if (answer.Data is not { } data) return ApiResults.RecordNotFound();
            var html = await renderer.RenderAsync<BillMembersFragment>(new Dictionary<string, object?> { [nameof(BillMembersFragment.Data)] = data });
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
