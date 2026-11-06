using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.Records;

public static class RelatedEndpoints
{
    public static void MapRelated(this IEndpointRouteBuilder routes) =>
        routes.MapGroup("/v1").MapGet("/records/{kind}/{recordId}/related", (string kind, string recordId, HttpContext http, RelatedStore related, CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(async () =>
            {
                var request = RelatedRequest.Parse(kind, recordId, http.Request.Query);
                if (!PublicationProtocol.IsAnyRecordId(recordId) || await related.ListAsync(request, cancellationToken) is not { } page) return ApiResults.RecordNotFound();
                return request.Cursor is { } cursor && cursor.Release != page.Release ? ApiResults.ReleaseChanged(page.Release) : ApiResults.Json(page);
            }));
}
