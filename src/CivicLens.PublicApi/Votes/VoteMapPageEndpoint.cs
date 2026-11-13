using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Pages;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Votes;

/// <summary>The gated full map page of one vote. It reads the same vote page snapshot as the vote page, so the map and the vote agree. Its ETag uses that release.</summary>
public static class VoteMapPageEndpoint
{
    public static Task<IResult> RespondAsync(string recordId, HttpContext context, VotePageReader reader, ServerPageResponder responder,
        PublicBaseUrl baseUrl, CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
    {
        var rootPath = baseUrl.Join(string.Empty);
        if (PublicationProtocol.IsAnyRecordId(recordId) && await reader.ReadAsync(recordId, cancellationToken) is { } vote)
            return await responder.RespondAsync<VoteMapPage>(context, StatusCodes.Status200OK, vote.Release, new Dictionary<string, object?>
            {
                [nameof(VoteMapPage.Data)] = vote,
                [nameof(VoteMapPage.RootPath)] = rootPath
            }, cancellationToken);
        return await responder.RespondAsync<PublishedNotFoundPage>(context, StatusCodes.Status404NotFound,
            new Dictionary<string, object?> { [nameof(PublishedNotFoundPage.RootPath)] = rootPath }, cancellationToken);
    });
}
