using CivicLens.PublicApi.Home;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Votes;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.Pages;

public static class ServerPageEndpoints
{
    /// <summary>The path prefix of the server pages. No live route uses it, so the static pages stay the production path until a route moves.</summary>
    public const string Prefix = "/web";

    /// <summary>Maps the gated home, Compare, and vote previews and the not found page under the server-page prefix.</summary>
    public static void MapServerPages(this IEndpointRouteBuilder routes, PublicBaseUrl baseUrl)
    {
        routes.MapGet(Prefix, (HttpContext context, HomePageReader home, ServerPageResponder responder, CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(async () => await home.ReadAsync(cancellationToken) is { } answer
                ? await responder.RespondAsync<CivicLens.PublicWeb.Publication.HomePage>(context, StatusCodes.Status200OK, answer.Release,
                    new Dictionary<string, object?>
                    {
                        [nameof(CivicLens.PublicWeb.Publication.HomePage.Data)] = answer,
                        [nameof(CivicLens.PublicWeb.Publication.HomePage.RootPath)] = baseUrl.Join(string.Empty),
                        [nameof(CivicLens.PublicWeb.Publication.HomePage.BrowserApiUrl)] = home.BrowserApiRoot
                    }, cancellationToken)
                : ApiResults.NoRelease()));
        routes.MapGet(Prefix + "/compare", (HttpContext context, HomePageReader home, ServerPageResponder responder, CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(() => responder.RespondAsync<ComparePage>(context, StatusCodes.Status200OK,
                new Dictionary<string, object?>
                {
                    [nameof(ComparePage.RootPath)] = baseUrl.Join(string.Empty),
                    [nameof(ComparePage.BrowserApiUrl)] = home.BrowserApiRoot
                }, cancellationToken)));
        routes.MapGet(VoteRoute("{recordId}"), (string recordId, HttpContext context, VotePageReader votes, ServerPageResponder responder,
            CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
        {
            var filter = new VoteMemberFilter(QueryValues.FilterValue(QueryValues.Value(context.Request.Query, "q")?.Trim(), "q"), QueryValues.Filter(context.Request.Query, "cast"),
                QueryValues.Filter(context.Request.Query, "party"));
            if (!PublicationProtocol.IsAnyRecordId(recordId) || await votes.ReadAsync(recordId, cancellationToken) is not { } page)
                return await NotFoundAsync(context, responder, baseUrl, cancellationToken);
            return await responder.RespondAsync<VoteDetailPage>(context, StatusCodes.Status200OK, page.Release, new Dictionary<string, object?>
            {
                [nameof(VoteDetailPage.Data)] = page,
                [nameof(VoteDetailPage.RootPath)] = baseUrl.Join(string.Empty),
                [nameof(VoteDetailPage.PagePath)] = VoteRoute(recordId),
                [nameof(VoteDetailPage.Filter)] = filter
            }, cancellationToken);
        }));
        routes.MapGet(Prefix + "/{**path}", (HttpContext context, ServerPageResponder responder, CancellationToken cancellationToken) =>
            NotFoundAsync(context, responder, baseUrl, cancellationToken));
    }

    /// <summary>The route of the server-rendered vote page. The page uses it as an origin-relative form action, so a filter stays on the origin that served the page. The public base URL can name another origin.</summary>
    private static string VoteRoute(string recordId) => $"{Prefix}/{PublicationProtocol.RecordPagePath(recordId)}";

    private static Task<IResult> NotFoundAsync(HttpContext context, ServerPageResponder responder, PublicBaseUrl baseUrl, CancellationToken cancellationToken) =>
        responder.RespondAsync<PublishedNotFoundPage>(context, StatusCodes.Status404NotFound,
            new Dictionary<string, object?> { [nameof(PublishedNotFoundPage.RootPath)] = baseUrl.Join(string.Empty) }, cancellationToken);
}
