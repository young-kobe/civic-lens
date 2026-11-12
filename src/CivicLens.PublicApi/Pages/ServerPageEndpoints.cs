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
using CivicLens.PublicApi.Search;
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
    /// <summary>The path prefix of the server pages. No live route uses it, so the static pages stay the production path until a route moves.</summary>
    public const string Prefix = "/web";

    /// <summary>Maps the gated home, Compare, officials, boards, search, vote kind index, official, issue, and record previews and the not found page under the server-page prefix.</summary>
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
        routes.MapFixedPages(baseUrl);
        routes.MapGet(Prefix + "/officials", OfficialsPageEndpoint.RespondAsync);
        routes.MapGet(Prefix + "/boards", BoardsPageEndpoint.RespondAsync);
        routes.MapGet(Prefix + "/search", SearchPageEndpoint.RespondAsync);
        routes.MapGet(Prefix + "/issues/{issueId}", (string issueId, HttpContext context, IssuePageReader issues, HomePageReader home, ServerPageResponder responder,
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
        routes.MapGet(Prefix + "/" + PublicationProtocol.VoteKindsPagePath, (HttpContext context, ServerPageResponder responder, CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(() => responder.RespondAsync<VoteKindsPage>(context, StatusCodes.Status200OK,
                new Dictionary<string, object?> { [nameof(VoteKindsPage.RootPath)] = baseUrl.Join(string.Empty) }, cancellationToken)));
        routes.MapGet(Prefix + "/officials/{id}", (string id, HttpContext context, OfficialPageReader officials, ServerPageResponder responder, CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(async () => PublicationProtocol.IsPathSegment(id) && await officials.ReadAsync(id, cancellationToken) is { } page
                ? await RespondOfficialAsync(context, responder, page, baseUrl, cancellationToken)
                : await NotFoundAsync(context, responder, baseUrl, cancellationToken)));
        routes.MapGet(RecordRoute("{recordId}"), (string recordId, HttpContext context, RecordKindStore kinds, VotePageReader votes, BillPageReader bills, StatementPageReader statements,
            ExecutiveActionPageReader actions, DocumentChangePageReader changes, BriefPageReader briefs, WithdrawalStore withdrawals, ServerPageResponder responder,
            CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(async () =>
        {
            var rootPath = baseUrl.Join(string.Empty);
            var kind = PublicationProtocol.IsAnyRecordId(recordId) ? await kinds.FindAsync(recordId, cancellationToken) : null;
            switch (kind)
            {
                case PublishedRecordKind.Vote when await votes.ReadAsync(recordId, cancellationToken) is { } vote:
                    var filter = new VoteMemberFilter(QueryValues.FilterValue(QueryValues.Value(context.Request.Query, "q")?.Trim(), "q"), QueryValues.Filter(context.Request.Query, "cast"),
                        QueryValues.Filter(context.Request.Query, "party"));
                    return await responder.RespondAsync<VoteDetailPage>(context, StatusCodes.Status200OK, vote.Release, new Dictionary<string, object?>
                    {
                        [nameof(VoteDetailPage.Data)] = vote,
                        [nameof(VoteDetailPage.RootPath)] = rootPath,
                        [nameof(VoteDetailPage.PagePath)] = RecordRoute(recordId),
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
        routes.MapGet(Prefix + "/{**path}", (HttpContext context, ServerPageResponder responder, CancellationToken cancellationToken) =>
            NotFoundAsync(context, responder, baseUrl, cancellationToken));
    }

    /// <summary>The route of the server-rendered record page. One route serves every kind, because a record ID does not name its kind. The vote page uses it as an origin-relative form action, so a filter stays on the origin that served the page. The public base URL can name another origin.</summary>
    private static string RecordRoute(string recordId) => $"{Prefix}/{PublicationProtocol.RecordPagePath(recordId)}";

    private static Task<IResult> NotFoundAsync(HttpContext context, ServerPageResponder responder, PublicBaseUrl baseUrl, CancellationToken cancellationToken) =>
        NotFoundAsync(context, responder, baseUrl.Join(string.Empty), cancellationToken);

    internal static Task<IResult> NotFoundAsync(HttpContext context, ServerPageResponder responder, string rootPath, CancellationToken cancellationToken) =>
        responder.RespondAsync<PublishedNotFoundPage>(context, StatusCodes.Status404NotFound,
            new Dictionary<string, object?> { [nameof(PublishedNotFoundPage.RootPath)] = rootPath }, cancellationToken);

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
