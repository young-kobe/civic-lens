using CivicLens.PublicApi.Bills;
using CivicLens.PublicApi.Boards;
using CivicLens.PublicApi.Briefs;
using CivicLens.PublicApi.DocumentChanges;
using CivicLens.PublicApi.ExecutiveActions;
using CivicLens.PublicApi.Home;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Issues;
using CivicLens.PublicApi.Officials;
using CivicLens.PublicApi.Records;
using CivicLens.PublicApi.Relationships;
using CivicLens.PublicApi.Search;
using CivicLens.PublicApi.Sitemap;
using CivicLens.PublicApi.Statements;
using CivicLens.PublicApi.Votes;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.Pages;

public static class ServerPageEndpoints
{
    /// <summary>The path of the API. The not found page leaves an unknown path under it to the API, which answers with no page.</summary>
    private const string ApiPath = "/v1";

    /// <summary>The route of a public path. Every server page, file, and redirect is mapped at the path that <see cref="PublicationProtocol"/> names for it.</summary>
    internal static string Route(string publicPath) => "/" + publicPath;

    /// <summary>Maps the home, Compare, sitemap, robots file, officials, boards, the four full board lists, search, the four per-kind lists, vote kind index, official, issue, and record pages,
    /// the vote map page, the redirects of old paths, and the not found page, each at its public path.</summary>
    public static void MapServerPages(this IEndpointRouteBuilder routes, PublicBaseUrl baseUrl)
    {
        var homePage = (HttpContext context, HomePageReader home, ServerPageResponder responder, CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(async () => await home.ReadAsync(cancellationToken) is { } read
                ? await responder.RespondAsync<CivicLens.PublicWeb.Publication.HomePage>(context, StatusCodes.Status200OK, read.Freshness,
                    async () => new Dictionary<string, object?>
                    {
                        [nameof(CivicLens.PublicWeb.Publication.HomePage.Data)] = await home.WithDetailAsync(read.Data, cancellationToken),
                        [nameof(CivicLens.PublicWeb.Publication.HomePage.RootPath)] = baseUrl.Join(string.Empty),
                        [nameof(CivicLens.PublicWeb.Publication.HomePage.BrowserApiUrl)] = home.BrowserApiRoot
                    }, cancellationToken)
                : ApiResults.NoRelease());
        routes.MapGet("/", homePage);
        routes.MapGet(Route(PublicationProtocol.HomePath), homePage);
        routes.MapGet(Route(PublicationProtocol.ComparePagePath), (HttpContext context, HomePageReader home, ServerPageResponder responder, CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(() => responder.RespondAsync<ComparePage>(context, StatusCodes.Status200OK,
                new Dictionary<string, object?>
                {
                    [nameof(ComparePage.RootPath)] = baseUrl.Join(string.Empty),
                    [nameof(ComparePage.BrowserApiUrl)] = home.BrowserApiRoot
                }, cancellationToken)));
        routes.MapFixedPages(baseUrl);
        routes.MapSitemap(baseUrl);
        routes.MapGet(Route(PublicationProtocol.ListPagePath(PublishedRecordKind.Vote)), ListPageEndpoints.VotesAsync);
        routes.MapGet(Route(PublicationProtocol.ListPagePath(PublishedRecordKind.Action)), ListPageEndpoints.BillsAsync);
        routes.MapGet(Route(PublicationProtocol.ListPagePath(PublishedRecordKind.Statement)), ListPageEndpoints.StatementsAsync);
        routes.MapGet(Route(PublicationProtocol.ListPagePath(PublishedRecordKind.ExecutiveAction)), ListPageEndpoints.ExecutiveActionsAsync);
        routes.MapGet(Route(PublicationProtocol.OfficialsPagePath), OfficialsPageEndpoint.RespondAsync);
        routes.MapGet(Route(PublicationProtocol.BoardsPagePath), BoardsPageEndpoint.RespondAsync);
        foreach (var board in OfficialBoard.All)
            routes.MapGet(Route(board.PagePath), (HttpContext context, BoardsPageReader reader, ServerPageResponder responder,
                CancellationToken cancellationToken) => BoardsPageEndpoint.RespondBoardAsync(board, context, reader, responder, baseUrl, cancellationToken));
        routes.MapGet(Route(PublicationProtocol.SearchPagePath), SearchPageEndpoint.RespondAsync);
        routes.MapGet(Route(PublicationProtocol.IssuePagePath("{issueId}", 1)), (string issueId, HttpContext context, IssuePageReader issues, HomePageReader home, ServerPageResponder responder,
            CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(async () => await issues.ReadAsync(issueId, cancellationToken) is { } data
                ? await responder.RespondAsync<IssuePage>(context, StatusCodes.Status200OK, data.Release,
                    new Dictionary<string, object?>
                    {
                        [nameof(IssuePage.Data)] = data,
                        [nameof(IssuePage.RootPath)] = baseUrl.Join(string.Empty),
                        [nameof(IssuePage.BrowserApiUrl)] = home.BrowserApiRoot
                    }, cancellationToken)
                : await NotFoundAsync(context, responder, baseUrl, cancellationToken)));
        routes.MapGet(Route(PublicationProtocol.VoteKindsPagePath), (HttpContext context, ServerPageResponder responder, CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(() => responder.RespondAsync<VoteKindsPage>(context, StatusCodes.Status200OK,
                new Dictionary<string, object?> { [nameof(VoteKindsPage.RootPath)] = baseUrl.Join(string.Empty) }, cancellationToken)));
        routes.MapGet(Route(PublicationProtocol.OfficialPagePath("{id}")), (string id, HttpContext context, OfficialPageReader officials, HomePageReader home, ServerPageResponder responder,
            CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(async () => PublicationProtocol.IsPathSegment(id) && await officials.ReadAsync(id, cancellationToken) is { } page
                ? await RespondOfficialAsync(context, responder, page, baseUrl, home.BrowserApiRoot, cancellationToken)
                : await NotFoundAsync(context, responder, baseUrl, cancellationToken)));
        routes.MapGet(Route(PublicationProtocol.VoteMapPagePath("{recordId}")), VoteMapPageEndpoint.RespondAsync);
        routes.MapGet(Route(PublicationProtocol.OfficialRecordPagePath("{id}")), (string id, HttpContext context, OfficialRecordPageReader records, ServerPageResponder responder,
            CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(async () =>
        {
            var request = OfficialRecordRequest.Parse(context.Request.Query);
            if (!PublicationProtocol.IsPathSegment(id) || await records.ReadAsync(id, request, cancellationToken) is not { } page)
                return await NotFoundAsync(context, responder, baseUrl, cancellationToken);
            // A cursor of a release that is no longer active starts the same kind again.
            if (request.Cursor is { } cursor && cursor.Release != page.Release)
                return FirstPage(baseUrl, PublicationProtocol.OfficialRecordPagePath(id), context.Request.Query);
            return await responder.RespondAsync<OfficialRecordPage>(context, StatusCodes.Status200OK, page.Release, new Dictionary<string, object?>
            {
                [nameof(OfficialRecordPage.Data)] = page,
                [nameof(OfficialRecordPage.RootPath)] = baseUrl.Join(string.Empty)
            }, cancellationToken);
        }));
        routes.MapGet(Route(PublicationProtocol.RecordPagePath("{recordId}")), (string recordId, HttpContext context, RecordKindStore kinds, VotePageReader votes, BillPageReader bills, StatementPageReader statements,
            ExecutiveActionPageReader actions, DocumentChangePageReader changes, BriefPageReader briefs, RelationshipPageReader relationships, WithdrawalStore withdrawals, ServerPageResponder responder,
            CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(async () =>
        {
            var rootPath = baseUrl.Join(string.Empty);
            var kind = PublicationProtocol.IsAnyRecordId(recordId) ? await kinds.FindAsync(recordId, cancellationToken) : null;
            switch (kind)
            {
                case PublishedRecordKind.Vote when await votes.ReadRecordPageAsync(recordId, cancellationToken) is { } vote:
                    var filter = new VoteMemberFilter(QueryValues.FilterValue(QueryValues.Value(context.Request.Query, "q")?.Trim(), "q"), QueryValues.Filter(context.Request.Query, "cast"),
                        QueryValues.Filter(context.Request.Query, "party"));
                    return await responder.RespondAsync<VoteDetailPage>(context, StatusCodes.Status200OK, vote.Release, new Dictionary<string, object?>
                    {
                        [nameof(VoteDetailPage.Data)] = vote,
                        [nameof(VoteDetailPage.RootPath)] = rootPath,
                        [nameof(VoteDetailPage.Filter)] = filter
                    }, cancellationToken);
                case PublishedRecordKind.Action when await bills.ReadAsync(recordId, cancellationToken) is { } bill:
                    return await responder.RespondAsync<BillDetailPage>(context, StatusCodes.Status200OK, bill.Release, new Dictionary<string, object?>
                    {
                        [nameof(BillDetailPage.Data)] = bill,
                        [nameof(BillDetailPage.RootPath)] = rootPath
                    }, cancellationToken);
                case PublishedRecordKind.Statement when await statements.ReadAsync(recordId, cancellationToken) is { } statement:
                    return await responder.RespondAsync<StatementDetailPage>(context, StatusCodes.Status200OK, statement.Release, new Dictionary<string, object?>
                    {
                        [nameof(StatementDetailPage.Data)] = statement,
                        [nameof(StatementDetailPage.RootPath)] = rootPath
                    }, cancellationToken);
                case PublishedRecordKind.ExecutiveAction when await actions.ReadAsync(recordId, cancellationToken) is { } action:
                    return await responder.RespondAsync<ExecutiveActionDetailPage>(context, StatusCodes.Status200OK, action.Release, new Dictionary<string, object?>
                    {
                        [nameof(ExecutiveActionDetailPage.Data)] = action,
                        [nameof(ExecutiveActionDetailPage.RootPath)] = rootPath
                    }, cancellationToken);
                case PublishedRecordKind.DocumentChange when await changes.ReadAsync(recordId, cancellationToken) is { } change:
                    return await responder.RespondAsync<DocumentChangeDetailPage>(context, StatusCodes.Status200OK, change.Release, new Dictionary<string, object?>
                    {
                        [nameof(DocumentChangeDetailPage.Data)] = change,
                        [nameof(DocumentChangeDetailPage.RootPath)] = rootPath
                    }, cancellationToken);
                case PublishedRecordKind.Brief when await briefs.ReadAsync(recordId, cancellationToken) is { } brief:
                    return await responder.RespondAsync<BriefDetailPage>(context, StatusCodes.Status200OK, brief.Release, new Dictionary<string, object?>
                    {
                        [nameof(BriefDetailPage.Data)] = brief,
                        [nameof(BriefDetailPage.RootPath)] = rootPath
                    }, cancellationToken);
                case PublishedRecordKind.Relationship when await relationships.ReadAsync(recordId, cancellationToken) is { } relationship:
                    return await responder.RespondAsync<RelationshipDetailPage>(context, StatusCodes.Status200OK, relationship.Release, new Dictionary<string, object?>
                    {
                        [nameof(RelationshipDetailPage.Data)] = relationship,
                        [nameof(RelationshipDetailPage.RootPath)] = rootPath
                    }, cancellationToken);
                case null when PublicationProtocol.IsAnyRecordId(recordId) && await withdrawals.FindAsync(recordId, cancellationToken) is { } notice:
                    return await responder.RespondAsync<PublishedWithdrawalPage>(context, StatusCodes.Status200OK, notice.Release, new Dictionary<string, object?>
                    {
                        [nameof(PublishedWithdrawalPage.Withdrawal)] = notice.Withdrawal,
                        [nameof(PublishedWithdrawalPage.RootPath)] = rootPath
                    }, cancellationToken);
                default:
                    return await NotFoundAsync(context, responder, baseUrl, cancellationToken);
            }
        }));
        routes.MapOldPageRedirects(baseUrl);
        routes.MapGet("/{**path}", (HttpContext context, ServerPageResponder responder, CancellationToken cancellationToken) =>
            context.Request.Path.StartsWithSegments(ApiPath, StringComparison.Ordinal)
                ? Task.FromResult(Results.NotFound())
                : NotFoundAsync(context, responder, baseUrl, cancellationToken));
    }

    /// <summary>The first page of the same filters at the public address of a paged page, for a cursor of a release that is no longer active.
    /// It names the public path, not the request path, so a proxy that rewrites the path cannot send the reader to an address the site does not serve.</summary>
    internal static IResult FirstPage(PublicBaseUrl baseUrl, string publicPath, IQueryCollection query) =>
        Results.Redirect(baseUrl.Join(publicPath) + QueryString.Create(query.Where(pair => pair.Key != "cursor")).ToUriComponent());

    private static Task<IResult> NotFoundAsync(HttpContext context, ServerPageResponder responder, PublicBaseUrl baseUrl, CancellationToken cancellationToken) =>
        NotFoundAsync(context, responder, baseUrl.Join(string.Empty), cancellationToken);

    internal static Task<IResult> NotFoundAsync(HttpContext context, ServerPageResponder responder, string rootPath, CancellationToken cancellationToken) =>
        responder.RespondAsync<PublishedNotFoundPage>(context, StatusCodes.Status404NotFound,
            new Dictionary<string, object?> { [nameof(PublishedNotFoundPage.RootPath)] = rootPath }, cancellationToken);

    /// <summary>An official with executive actions gets the President layout. Every other official gets the member layout.</summary>
    private static Task<IResult> RespondOfficialAsync(HttpContext context, ServerPageResponder responder, OfficialPageData page, PublicBaseUrl baseUrl,
        string? browserApiUrl, CancellationToken cancellationToken) => page.Executive is null
        ? responder.RespondAsync<OfficialProfilePage>(context, StatusCodes.Status200OK, page.Release,
            new Dictionary<string, object?>(OfficialParameters(page, baseUrl)) { [nameof(OfficialProfilePage.BrowserApiUrl)] = browserApiUrl }, cancellationToken)
        : responder.RespondAsync<PresidentPage>(context, StatusCodes.Status200OK, page.Release, OfficialParameters(page, baseUrl), cancellationToken);

    private static Dictionary<string, object?> OfficialParameters(OfficialPageData page, PublicBaseUrl baseUrl) => new()
    {
        [nameof(OfficialProfilePage.Data)] = page,
        [nameof(OfficialProfilePage.RootPath)] = baseUrl.Join(string.Empty)
    };
}
