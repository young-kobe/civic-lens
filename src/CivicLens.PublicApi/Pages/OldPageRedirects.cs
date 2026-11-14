using CivicLens.PublicApi.Http;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;

namespace CivicLens.PublicApi.Pages;

/// <summary>
/// The permanent redirects of the old static paths that have no server page: the pages of older Congresses of an official, the policy area pages,
/// the all records list, and the later issue pages. Each keeps the query of the old URL and names the public address of its target.
/// A path that the static site never wrote answers the not found page.
/// </summary>
public static class OldPageRedirects
{
    private const string PolicyAreaFilter = "policyArea";

    public static void MapOldPageRedirects(this IEndpointRouteBuilder routes, PublicBaseUrl baseUrl)
    {
        var rootPath = baseUrl.Join(string.Empty);
        // The votes list replaces the old all records list, as the plan of the cutover decided.
        var allRecordsTarget = rootPath + PublicationProtocol.ListPagePath(PublishedRecordKind.Vote);
        routes.MapGet(ServerPageEndpoints.Route("officials/{id}/{congress:int:min(1)}.html"), (string id, HttpContext context, ServerPageResponder responder,
            CancellationToken cancellationToken) => RedirectAsync(context, responder, rootPath,
                PublicationProtocol.IsPathSegment(id) ? rootPath + PublicationProtocol.OfficialPagePath(id) : null, cancellationToken));
        routes.MapGet(ServerPageEndpoints.Route("policy-areas/{file}.html"), (string file, HttpContext context, ServerPageResponder responder,
            CancellationToken cancellationToken) => RedirectAsync(context, responder, rootPath,
                PolicyAreaOf(file) is { } id ? QueryHelpers.AddQueryString(rootPath + PublicationProtocol.SearchPagePath, PolicyAreaFilter, id) : null, cancellationToken));
        routes.MapGet(ServerPageEndpoints.Route(PublicationProtocol.AllRecordsPagePath(1)), (HttpContext context) => Moved(allRecordsTarget, context.Request.Query));
        routes.MapGet(ServerPageEndpoints.Route("all-{page:int:min(2)}.html"), (HttpContext context) => Moved(allRecordsTarget, context.Request.Query));
        // The issue page route also matches a later page, with the whole file name as the issue. The lower order makes this route win when both match.
        routes.MapGet(ServerPageEndpoints.Route("issues/{id}.page-{page:int:min(2)}.html"), (string id, HttpContext context, ServerPageResponder responder,
            CancellationToken cancellationToken) => RedirectAsync(context, responder, rootPath,
                PublicationProtocol.IsPathSegment(id) ? rootPath + IssueRecordWords.ListPath(PublishedRecordKind.Vote, id) : null, cancellationToken))
            .WithOrder(-1);
    }

    /// <summary>The policy area of an old file name, "{id}" for the first page or "{id}.page-{n}" for a later page. Null when the static site never wrote that name.
    /// One route takes both names, because two routes of these shapes match the same later page and routing cannot choose between them.</summary>
    private static string? PolicyAreaOf(string file)
    {
        var mark = file.IndexOf(".page-", StringComparison.Ordinal);
        var id = mark < 0 ? file : file[..mark];
        if (!PublicationProtocol.IsPathSegment(id)) return null;
        if (mark < 0) return id;
        return int.TryParse(file[(mark + ".page-".Length)..], out var page) && page > 1 && PublicationProtocol.PolicyAreaPagePath(id, page) == $"policy-areas/{file}.html"
            ? id
            : null;
    }

    private static Task<IResult> RedirectAsync(HttpContext context, ServerPageResponder responder, string rootPath, string? target, CancellationToken cancellationToken) =>
        target is null
            ? ApiResults.GuardAsync(() => ServerPageEndpoints.NotFoundAsync(context, responder, rootPath, cancellationToken))
            : Task.FromResult(Moved(target, context.Request.Query));

    /// <summary>A permanent redirect that keeps every query value of the old URL. A value that the target names itself wins.</summary>
    private static IResult Moved(string target, IQueryCollection query)
    {
        var mark = target.IndexOf('?');
        var own = mark < 0 ? [] : QueryHelpers.ParseQuery(target[mark..]);
        var kept = query.Where(pair => !own.ContainsKey(pair.Key)).SelectMany(pair => pair.Value.Select(value => KeyValuePair.Create(pair.Key, value)));
        return Results.Redirect(QueryHelpers.AddQueryString(target, kept), permanent: true);
    }
}
