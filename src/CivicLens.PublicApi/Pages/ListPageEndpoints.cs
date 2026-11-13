using CivicLens.PublicApi.Bills;
using CivicLens.PublicApi.ExecutiveActions;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Statements;
using CivicLens.PublicApi.Votes;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Pages;

/// <summary>The gated per-kind list pages: votes, bills, statements, and executive actions. A page of one release, filter, and cursor never changes,
/// so each uses the shared page cache, as the officials list does. A cursor of another release redirects to the first page of the same filters.
/// A refused query answers 400 with the page and its form, not a JSON problem.</summary>
public static class ListPageEndpoints
{
    public static Task<IResult> VotesAsync(HttpContext context, VoteListReader reader, ServerPageResponder responder, PublicBaseUrl baseUrl, CancellationToken cancellationToken) =>
        ListAsync<VoteListPage, VoteRequest, VoteListPageData>(context, responder, baseUrl, VoteListReader.Parse, request => request.Cursor?.Release,
            reader.ReadAsync, reader.ReadRefusedAsync, data => data.Release, cancellationToken);

    public static Task<IResult> BillsAsync(HttpContext context, BillListReader reader, ServerPageResponder responder, PublicBaseUrl baseUrl, CancellationToken cancellationToken) =>
        ListAsync<BillListPage, BillRequest, BillListPageData>(context, responder, baseUrl, BillListReader.Parse, request => request.Cursor?.Release,
            reader.ReadAsync, reader.ReadRefusedAsync, data => data.Release, cancellationToken);

    public static Task<IResult> StatementsAsync(HttpContext context, StatementListReader reader, ServerPageResponder responder, PublicBaseUrl baseUrl, CancellationToken cancellationToken) =>
        ListAsync<StatementListPage, StatementRequest, StatementListPageData>(context, responder, baseUrl, StatementListReader.Parse, request => request.Cursor?.Release,
            reader.ReadAsync, reader.ReadRefusedAsync, data => data.Release, cancellationToken);

    public static Task<IResult> ExecutiveActionsAsync(HttpContext context, ExecutiveActionListReader reader, ServerPageResponder responder, PublicBaseUrl baseUrl,
        CancellationToken cancellationToken) =>
        ListAsync<ExecutiveActionListPage, ExecutiveActionListRequest, ExecutiveActionListPageData>(context, responder, baseUrl, ExecutiveActionListReader.Parse,
            request => request.Cursor?.Release, reader.ReadAsync, reader.ReadRefusedAsync, data => data.Release, cancellationToken);

    /// <summary>The one flow of every list page. A query that the parser refuses renders the page with status 400, the typed text kept in the form,
    /// and the refused field marked, instead of a JSON problem. The exception names the field.</summary>
    private static Task<IResult> ListAsync<TPage, TRequest, TData>(HttpContext context, ServerPageResponder responder, PublicBaseUrl baseUrl,
        Func<IQueryCollection, TRequest> parse, Func<TRequest, int?> cursorRelease, Func<TRequest, CancellationToken, Task<TData?>> read,
        Func<IQueryCollection, string?, CancellationToken, Task<TData?>> readRefused, Func<TData, int> release, CancellationToken cancellationToken)
        where TPage : IComponent where TData : class =>
        ApiResults.GuardAsync(async () =>
        {
            TRequest request;
            try
            {
                request = parse(context.Request.Query);
            }
            catch (ArgumentException refused)
            {
                return await readRefused(context.Request.Query, refused.ParamName, cancellationToken) is { } refusedData
                    ? await RespondAsync<TPage>(context, responder, baseUrl, StatusCodes.Status400BadRequest, release(refusedData), refusedData, cancellationToken)
                    : ApiResults.NoRelease();
            }
            return await read(request, cancellationToken) is not { } data ? ApiResults.NoRelease()
                : cursorRelease(request) is { } cursor && cursor != release(data) ? FirstPage(context.Request)
                : await RespondAsync<TPage>(context, responder, baseUrl, StatusCodes.Status200OK, release(data), data, cancellationToken);
        });

    /// <summary>Every list page component takes the same two parameters, named <c>Data</c> and <c>RootPath</c>.</summary>
    private static Task<IResult> RespondAsync<TPage>(HttpContext context, ServerPageResponder responder, PublicBaseUrl baseUrl, int statusCode, int release, object data,
        CancellationToken cancellationToken) where TPage : IComponent =>
        responder.RespondAsync<TPage>(context, statusCode, release,
            new Dictionary<string, object?> { ["Data"] = data, ["RootPath"] = baseUrl.Join(string.Empty) }, cancellationToken);

    /// <summary>The same filters without the cursor of a release that is no longer active.</summary>
    private static IResult FirstPage(HttpRequest request) =>
        Results.Redirect(request.Path + QueryString.Create(request.Query.Where(pair => pair.Key != "cursor")).ToUriComponent());
}
