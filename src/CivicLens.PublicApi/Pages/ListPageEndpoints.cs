using CivicLens.PublicApi.Bills;
using CivicLens.PublicApi.ExecutiveActions;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Statements;
using CivicLens.PublicApi.Votes;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Pages;

/// <summary>The gated per-kind list pages: votes, bills, statements, and executive actions. A page of one release, filter, and cursor never changes,
/// so each uses the shared page cache, as the officials list does. A cursor of another release redirects to the first page of the same filters.</summary>
public static class ListPageEndpoints
{
    public static Task<IResult> VotesAsync(HttpContext context, VoteListReader reader, ServerPageResponder responder, PublicBaseUrl baseUrl, CancellationToken cancellationToken) =>
        ApiResults.GuardAsync(async () =>
        {
            var request = VoteListReader.Parse(context.Request.Query);
            return await reader.ReadAsync(request, cancellationToken) is not { } data ? ApiResults.NoRelease()
                : request.Cursor is { } cursor && cursor.Release != data.Release ? FirstPage(context.Request)
                : await RespondAsync<VoteListPage>(context, responder, baseUrl, data.Release, data, cancellationToken);
        });

    public static Task<IResult> BillsAsync(HttpContext context, BillListReader reader, ServerPageResponder responder, PublicBaseUrl baseUrl, CancellationToken cancellationToken) =>
        ApiResults.GuardAsync(async () =>
        {
            var request = BillListReader.Parse(context.Request.Query);
            return await reader.ReadAsync(request, cancellationToken) is not { } data ? ApiResults.NoRelease()
                : request.Cursor is { } cursor && cursor.Release != data.Release ? FirstPage(context.Request)
                : await RespondAsync<BillListPage>(context, responder, baseUrl, data.Release, data, cancellationToken);
        });

    public static Task<IResult> StatementsAsync(HttpContext context, StatementListReader reader, ServerPageResponder responder, PublicBaseUrl baseUrl, CancellationToken cancellationToken) =>
        ApiResults.GuardAsync(async () =>
        {
            var request = StatementListReader.Parse(context.Request.Query);
            return await reader.ReadAsync(request, cancellationToken) is not { } data ? ApiResults.NoRelease()
                : request.Cursor is { } cursor && cursor.Release != data.Release ? FirstPage(context.Request)
                : await RespondAsync<StatementListPage>(context, responder, baseUrl, data.Release, data, cancellationToken);
        });

    public static Task<IResult> ExecutiveActionsAsync(HttpContext context, ExecutiveActionListReader reader, ServerPageResponder responder, PublicBaseUrl baseUrl,
        CancellationToken cancellationToken) =>
        ApiResults.GuardAsync(async () =>
        {
            var request = ExecutiveActionListReader.Parse(context.Request.Query);
            return await reader.ReadAsync(request, cancellationToken) is not { } data ? ApiResults.NoRelease()
                : request.Cursor is { } cursor && cursor.Release != data.Release ? FirstPage(context.Request)
                : await RespondAsync<ExecutiveActionListPage>(context, responder, baseUrl, data.Release, data, cancellationToken);
        });

    /// <summary>Every list page component takes the same two parameters, named <c>Data</c> and <c>RootPath</c>.</summary>
    private static Task<IResult> RespondAsync<TPage>(HttpContext context, ServerPageResponder responder, PublicBaseUrl baseUrl, int release, object data,
        CancellationToken cancellationToken) where TPage : Microsoft.AspNetCore.Components.IComponent =>
        responder.RespondAsync<TPage>(context, StatusCodes.Status200OK, release,
            new Dictionary<string, object?> { ["Data"] = data, ["RootPath"] = baseUrl.Join(string.Empty) }, cancellationToken);

    /// <summary>The same filters without the cursor of a release that is no longer active.</summary>
    private static IResult FirstPage(HttpRequest request) =>
        Results.Redirect(request.Path + QueryString.Create(request.Query.Where(pair => pair.Key != "cursor")).ToUriComponent());
}
