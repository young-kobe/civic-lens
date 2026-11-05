using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;

namespace CivicLens.PublicApi.Votes;

public static class MemberVoteEndpoints
{
    /// <summary>The release changes at most once an hour, so a shared cache may repeat an answer for an hour, as it does for the ZIP answer.</summary>
    private const string CacheControl = "public, max-age=3600";

    public static void MapMemberVotes(this IEndpointRouteBuilder routes)
    {
        var v1 = routes.MapGroup("/v1");
        v1.MapGet("/member-votes", (HttpContext http, MemberVoteStore votes, CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
        {
            var request = MemberVoteRequest.Parse(http.Request.Query);
            if (await votes.FindAsync(request, cancellationToken) is not { } answer) return ApiResults.NoRelease();
            http.Response.Headers[HeaderNames.CacheControl] = CacheControl;
            return ApiResults.Json(answer);
        }));
    }
}
