using CivicLens.PublicApi.Home;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Officials;
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

    /// <summary>Maps the gated home, Compare, and official previews and the not found page under the server-page prefix.</summary>
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
        routes.MapGet(Prefix + "/officials/{id}", (string id, HttpContext context, OfficialPageReader officials, ServerPageResponder responder, CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(async () => PublicationProtocol.IsPathSegment(id) && await officials.ReadAsync(id, cancellationToken) is { } page
                ? await RespondOfficialAsync(context, responder, page, baseUrl, cancellationToken)
                : await RespondNotFoundAsync(context, responder, baseUrl, cancellationToken)));
        routes.MapGet(Prefix + "/{**path}", (HttpContext context, ServerPageResponder responder, CancellationToken cancellationToken) =>
            RespondNotFoundAsync(context, responder, baseUrl, cancellationToken));
    }

    private static Task<IResult> RespondNotFoundAsync(HttpContext context, ServerPageResponder responder, PublicBaseUrl baseUrl, CancellationToken cancellationToken) =>
        responder.RespondAsync<PublishedNotFoundPage>(context, StatusCodes.Status404NotFound,
            new Dictionary<string, object?> { [nameof(PublishedNotFoundPage.RootPath)] = baseUrl.Join(string.Empty) }, cancellationToken);

    /// <summary>An official with executive actions gets the President layout. Every other official gets the member layout.</summary>
    private static Task<IResult> RespondOfficialAsync(HttpContext context, ServerPageResponder responder, OfficialPageData page, PublicBaseUrl baseUrl,
        CancellationToken cancellationToken) => page.Executive is null
        ? responder.RespondAsync<OfficialProfilePage>(context, StatusCodes.Status200OK, page.Release, OfficialParameters(page, baseUrl), cancellationToken)
        : responder.RespondAsync<PresidentPage>(context, StatusCodes.Status200OK, page.Release, OfficialParameters(page, baseUrl), cancellationToken);

    private static Dictionary<string, object?> OfficialParameters(OfficialPageData page, PublicBaseUrl baseUrl) => new()
    {
        [nameof(OfficialProfilePage.Data)] = page,
        [nameof(OfficialProfilePage.RootPath)] = baseUrl.Join(string.Empty)
    };
}
