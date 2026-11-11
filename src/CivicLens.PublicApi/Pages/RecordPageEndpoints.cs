using CivicLens.PublicApi.DocumentChanges;
using CivicLens.PublicApi.ExecutiveActions;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Statements;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.Pages;

/// <summary>Maps the gated server-rendered pages of the statement, executive action, and document change records. A record ID does not name its kind, so each kind has its own preview path until the live route can look up the kind.</summary>
public static class RecordPageEndpoints
{
    public const string StatementPath = "statements";
    public const string ExecutiveActionPath = "executive-actions";
    public const string DocumentChangePath = "document-changes";

    public static void MapRecordPages(this IEndpointRouteBuilder routes, PublicBaseUrl baseUrl)
    {
        MapPage<StatementDetailPage, StatementPageReader, StatementPageData>(routes, baseUrl, StatementPath, (reader, id, token) => reader.ReadAsync(id, token), data => data.Release);
        MapPage<ExecutiveActionDetailPage, ExecutiveActionPageReader, ExecutiveActionPageData>(routes, baseUrl, ExecutiveActionPath, (reader, id, token) => reader.ReadAsync(id, token), data => data.Release);
        MapPage<DocumentChangeDetailPage, DocumentChangePageReader, DocumentChangePageData>(routes, baseUrl, DocumentChangePath, (reader, id, token) => reader.ReadAsync(id, token), data => data.Release);
    }

    private static void MapPage<TPage, TReader, TData>(IEndpointRouteBuilder routes, PublicBaseUrl baseUrl, string path,
        Func<TReader, string, CancellationToken, Task<TData?>> read, Func<TData, int> release)
        where TPage : IComponent where TReader : notnull where TData : class =>
        routes.MapGet($"{ServerPageEndpoints.Prefix}/{path}/{{recordId}}", (string recordId, HttpContext context, TReader reader, ServerPageResponder responder,
            CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
        {
            if (!PublicationProtocol.IsAnyRecordId(recordId) || await read(reader, recordId, cancellationToken) is not { } data)
                return await responder.RespondAsync<PublishedNotFoundPage>(context, StatusCodes.Status404NotFound,
                    new Dictionary<string, object?> { [nameof(PublishedNotFoundPage.RootPath)] = baseUrl.Join(string.Empty) }, cancellationToken);
            return await responder.RespondAsync<TPage>(context, StatusCodes.Status200OK, release(data), new Dictionary<string, object?>
            {
                ["Data"] = data,
                ["RootPath"] = baseUrl.Join(string.Empty)
            }, cancellationToken);
        }));
}
