using CivicLens.PublicApi.Briefs;
using CivicLens.PublicApi.DocumentChanges;
using CivicLens.PublicApi.ExecutiveActions;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Records;
using CivicLens.PublicApi.Statements;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.Pages;

/// <summary>Maps the gated server-rendered pages of the statement, executive action, document change, and brief records. A record ID does not name its kind, so each kind has its own preview path until the live route can look up the kind.
/// A record that the active release withdrew has no record page. Its route answers the withdrawal notice instead, as the static page does.</summary>
public static class RecordPageEndpoints
{
    public const string StatementPath = "statements";
    public const string ExecutiveActionPath = "executive-actions";
    public const string DocumentChangePath = "document-changes";
    public const string BriefPath = "briefs";

    public static void MapRecordPages(this IEndpointRouteBuilder routes, PublicBaseUrl baseUrl)
    {
        MapPage<StatementDetailPage, StatementPageReader, StatementPageData>(routes, baseUrl, StatementPath, PublishedRecordKind.Statement, (reader, id, token) => reader.ReadAsync(id, token), data => data.Release);
        MapPage<ExecutiveActionDetailPage, ExecutiveActionPageReader, ExecutiveActionPageData>(routes, baseUrl, ExecutiveActionPath, PublishedRecordKind.ExecutiveAction, (reader, id, token) => reader.ReadAsync(id, token), data => data.Release);
        MapPage<DocumentChangeDetailPage, DocumentChangePageReader, DocumentChangePageData>(routes, baseUrl, DocumentChangePath, PublishedRecordKind.DocumentChange, (reader, id, token) => reader.ReadAsync(id, token), data => data.Release);
        MapPage<BriefDetailPage, BriefPageReader, BriefPageData>(routes, baseUrl, BriefPath, PublishedRecordKind.Brief, (reader, id, token) => reader.ReadAsync(id, token), data => data.Release);
    }

    private static void MapPage<TPage, TReader, TData>(IEndpointRouteBuilder routes, PublicBaseUrl baseUrl, string path, PublishedRecordKind kind,
        Func<TReader, string, CancellationToken, Task<TData?>> read, Func<TData, int> release)
        where TPage : IComponent where TReader : notnull where TData : class =>
        routes.MapGet($"{ServerPageEndpoints.Prefix}/{path}/{{recordId}}", (string recordId, HttpContext context, TReader reader, WithdrawalStore withdrawals, ServerPageResponder responder,
            CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
        {
            if (!PublicationProtocol.IsAnyRecordId(recordId)) return await ServerPageEndpoints.NotFoundAsync(context, responder, baseUrl, cancellationToken);
            if (await read(reader, recordId, cancellationToken) is not { } data)
                return await WithdrawnOrNotFoundAsync(context, withdrawals, responder, baseUrl, kind, recordId, cancellationToken);
            return await responder.RespondAsync<TPage>(context, StatusCodes.Status200OK, release(data), new Dictionary<string, object?>
            {
                [nameof(StatementDetailPage.Data)] = data,
                [nameof(StatementDetailPage.RootPath)] = baseUrl.Join(string.Empty)
            }, cancellationToken);
        }));

    /// <summary>The record read found nothing, so the withdrawal view is the one more place that can hold the record. It is a second read, and the notice then comes from its own release.</summary>
    private static async Task<IResult> WithdrawnOrNotFoundAsync(HttpContext context, WithdrawalStore withdrawals, ServerPageResponder responder,
        PublicBaseUrl baseUrl, PublishedRecordKind kind, string recordId, CancellationToken cancellationToken)
    {
        if (await withdrawals.FindAsync(kind, recordId, cancellationToken) is not { } notice)
            return await ServerPageEndpoints.NotFoundAsync(context, responder, baseUrl, cancellationToken);
        return await responder.RespondAsync<PublishedWithdrawalPage>(context, StatusCodes.Status200OK, notice.Release, new Dictionary<string, object?>
        {
            [nameof(PublishedWithdrawalPage.Withdrawal)] = notice.Withdrawal,
            [nameof(PublishedWithdrawalPage.RootPath)] = baseUrl.Join(string.Empty)
        }, cancellationToken);
    }
}
