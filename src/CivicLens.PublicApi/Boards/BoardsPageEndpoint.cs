using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Pages;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Boards;

/// <summary>The gated party boards page. Its ETag uses the release of the board snapshot, so the page and its validator agree.</summary>
public static class BoardsPageEndpoint
{
    public static Task<IResult> RespondAsync(HttpContext context, BoardsPageReader reader, ServerPageResponder responder,
        PublicBaseUrl baseUrl, CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
        await reader.ReadAsync(cancellationToken) is { } data
            ? await responder.RespondAsync<BoardsPage>(context, StatusCodes.Status200OK, data.Release,
                new Dictionary<string, object?>
                {
                    [nameof(BoardsPage.Data)] = data,
                    [nameof(BoardsPage.RootPath)] = baseUrl.Join(string.Empty)
                }, cancellationToken)
            : ApiResults.NoRelease());
}
