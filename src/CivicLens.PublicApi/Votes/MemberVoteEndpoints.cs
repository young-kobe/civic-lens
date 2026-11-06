using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.Votes;

public static class MemberVoteEndpoints
{
    public static void MapMemberVotes(this IEndpointRouteBuilder routes)
    {
        var v1 = routes.MapGroup("/v1");
        v1.MapGet("/member-votes", (HttpContext http, MemberVoteStore votes, CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
        {
            var request = MemberVoteRequest.Parse(http.Request.Query);
            if (await votes.FindAsync(request, cancellationToken) is not { } answer) return ApiResults.NoRelease();
            return ApiResults.Json(answer);
        }));
    }
}
