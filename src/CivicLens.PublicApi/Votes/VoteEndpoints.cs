using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.Votes;

public static class VoteEndpoints
{
    public static void MapVotes(this IEndpointRouteBuilder routes)
    {
        var v1 = routes.MapGroup("/v1");
        v1.MapGet("/votes", (HttpContext http, VoteStore votes, CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
        {
            var request = VoteRequest.Parse(http.Request.Query);
            if (await votes.ListAsync(request, cancellationToken) is not { } page) return ApiResults.NoRelease();
            return request.Cursor is { } cursor && cursor.Release != page.Release ? ApiResults.ReleaseChanged(page.Release) : ApiResults.Json(page);
        }));
        v1.MapGet("/votes/{recordId}", (string recordId, VoteStore votes, CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
            PublicationProtocol.IsAnyRecordId(recordId) && await votes.FindAsync(recordId, cancellationToken) is { } vote
                ? ApiResults.Json(vote)
                : ApiResults.RecordNotFound()));
        v1.MapGet("/votes/{recordId}/members", (string recordId, HttpContext http, VoteStore votes, CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
        {
            if (!PublicationProtocol.IsAnyRecordId(recordId)) return ApiResults.RecordNotFound();
            var request = VoteMemberRequest.Parse(http.Request.Query);
            if (await votes.ListMembersAsync(recordId, request, cancellationToken) is not { } page) return ApiResults.RecordNotFound();
            return request.Cursor is { } cursor && cursor.Release != page.Release ? ApiResults.ReleaseChanged(page.Release) : ApiResults.Json(page);
        }));
    }
}
