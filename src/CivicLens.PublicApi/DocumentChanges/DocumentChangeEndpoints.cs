using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.DocumentChanges;

public static class DocumentChangeEndpoints
{
    public static void MapDocumentChanges(this IEndpointRouteBuilder routes) =>
        routes.MapGroup("/v1").MapGet("/document-changes/{recordId}", (string recordId, DocumentChangeStore changes, CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(async () =>
            {
                if (!PublicationProtocol.IsRecordId(PublishedRecordKind.DocumentChange, recordId)) return ApiResults.RecordNotFound();
                var result = await changes.FindAsync(recordId, cancellationToken);
                return result.Release is null ? ApiResults.NoRelease()
                    : result.Detail is null ? ApiResults.RecordNotFound() : ApiResults.Json(result.Detail);
            }));
}
