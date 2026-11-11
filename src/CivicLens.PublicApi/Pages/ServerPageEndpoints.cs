using CivicLens.PublicApi.Bills;
using CivicLens.PublicApi.Home;
using CivicLens.PublicApi.Http;
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

    /// <summary>Maps the gated home, Compare, and bill previews and the not found page under the server-page prefix.</summary>
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
        // The preview path of a static record page "records/{id}.html". A record that is no bill in the active release gets the not found page.
        routes.MapGet(Prefix + "/records/{recordId}.html", (string recordId, HttpContext context, BillPageReader reader, ServerPageResponder responder, CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(async () => PublicationProtocol.IsAnyRecordId(recordId) && await reader.ReadAsync(recordId, cancellationToken) is { } page
                ? await responder.RespondAsync<BillDetailPage>(context, StatusCodes.Status200OK, page.Release,
                    new Dictionary<string, object?>
                    {
                        [nameof(BillDetailPage.Data)] = page,
                        [nameof(BillDetailPage.RootPath)] = baseUrl.Join(string.Empty)
                    }, cancellationToken)
                : await responder.RespondAsync<PublishedNotFoundPage>(context, StatusCodes.Status404NotFound,
                    new Dictionary<string, object?> { [nameof(PublishedNotFoundPage.RootPath)] = baseUrl.Join(string.Empty) }, cancellationToken)));
        routes.MapGet(Prefix + "/{**path}", (HttpContext context, ServerPageResponder responder, CancellationToken cancellationToken) =>
            responder.RespondAsync<PublishedNotFoundPage>(context, StatusCodes.Status404NotFound,
                new Dictionary<string, object?> { [nameof(PublishedNotFoundPage.RootPath)] = baseUrl.Join(string.Empty) }, cancellationToken));
    }
}
