using CivicLens.PublicApi.Briefs;
using CivicLens.PublicApi.Home;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Issues;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.Pages;

/// <summary>The pages with fixed text and the two lists: About, Method, the glossary, the report page, the weekly briefs list, and the issues index.
/// Each route is the public path of its page.</summary>
public static class FixedPageEndpoints
{
    public static void MapFixedPages(this IEndpointRouteBuilder routes, PublicBaseUrl baseUrl)
    {
        var rootPath = baseUrl.Join(string.Empty);
        routes.MapGet(ServerPageEndpoints.Route(PublicationProtocol.AboutPagePath), (HttpContext context, ServerPageResponder responder, CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(() => responder.RespondAsync<AboutPage>(context, StatusCodes.Status200OK,
                new Dictionary<string, object?> { [nameof(AboutPage.RootPath)] = rootPath }, cancellationToken)));
        routes.MapGet(ServerPageEndpoints.Route(PublicationProtocol.MethodPagePath), (HttpContext context, ServerPageResponder responder, CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(() => responder.RespondAsync<MethodPage>(context, StatusCodes.Status200OK,
                new Dictionary<string, object?> { [nameof(MethodPage.RootPath)] = rootPath }, cancellationToken)));
        routes.MapGet(ServerPageEndpoints.Route(PublicationProtocol.GlossaryPagePath), (HttpContext context, ServerPageResponder responder, CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(() => responder.RespondAsync<PublishedGlossaryPage>(context, StatusCodes.Status200OK,
                new Dictionary<string, object?> { [nameof(PublishedGlossaryPage.RootPath)] = rootPath }, cancellationToken)));
        routes.MapGet(ServerPageEndpoints.Route(PublicationProtocol.ReportPagePath), (HttpContext context, HomePageReader home, PublicApiOptions options, ServerPageResponder responder,
            CancellationToken cancellationToken) => ApiResults.GuardAsync(() => responder.RespondAsync<PublishedReportPage>(context, StatusCodes.Status200OK,
                new Dictionary<string, object?>
                {
                    [nameof(PublishedReportPage.RootPath)] = rootPath,
                    [nameof(PublishedReportPage.PublicApiUrl)] = home.BrowserApiRoot,
                    [nameof(PublishedReportPage.TurnstileSiteKey)] = options.TurnstileSiteKey
                }, cancellationToken)));
        routes.MapGet(ServerPageEndpoints.Route(PublicationProtocol.BriefsPagePath(1)), (HttpContext context, BriefListReader briefs, ServerPageResponder responder,
            CancellationToken cancellationToken) => RespondBriefsAsync(1, context, briefs, responder, rootPath, cancellationToken));
        routes.MapGet(ServerPageEndpoints.Route("briefs-{page:int:min(2)}.html"), (int page, HttpContext context, BriefListReader briefs, ServerPageResponder responder,
            CancellationToken cancellationToken) => RespondBriefsAsync(page, context, briefs, responder, rootPath, cancellationToken));
        routes.MapGet(ServerPageEndpoints.Route(PublicationProtocol.FacetsPath), (HttpContext context, IssuesIndexReader issues, ServerPageResponder responder,
            CancellationToken cancellationToken) => ApiResults.GuardAsync(async () => await issues.ReadAsync(cancellationToken) is { } data
                ? await responder.RespondAsync<IssuesIndexPage>(context, StatusCodes.Status200OK, data.Release, new Dictionary<string, object?>
                {
                    [nameof(IssuesIndexPage.Data)] = data,
                    [nameof(IssuesIndexPage.RootPath)] = rootPath
                }, cancellationToken)
                : ApiResults.NoRelease()));
    }

    /// <summary>A page past the last brief has no page. It leaves the request to the shared not found page.</summary>
    private static Task<IResult> RespondBriefsAsync(int page, HttpContext context, BriefListReader briefs, ServerPageResponder responder, string rootPath,
        CancellationToken cancellationToken) => ApiResults.GuardAsync(async () => await briefs.ReadAsync(page, cancellationToken) is { } data
            ? await responder.RespondAsync<BriefsPage>(context, StatusCodes.Status200OK, data.Release, new Dictionary<string, object?>
            {
                [nameof(BriefsPage.Data)] = data,
                [nameof(BriefsPage.RootPath)] = rootPath
            }, cancellationToken)
            : await ServerPageEndpoints.NotFoundAsync(context, responder, rootPath, cancellationToken));
}
