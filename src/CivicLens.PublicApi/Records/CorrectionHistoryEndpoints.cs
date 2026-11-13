using CivicLens.PublicApi.Http;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.Records;

public static class CorrectionHistoryEndpoints
{
    public static void MapCorrectionHistories(this IEndpointRouteBuilder routes) =>
        routes.MapGroup("/v1").MapGet("/records/{kind}/{recordId}/corrections", (string kind, string recordId, CorrectionHistoryStore store, CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(async () =>
            {
                if (!RecordKinds.TryParse(kind, out var parsed) || !PublicationProtocol.IsRecordId(parsed, recordId))
                    return ApiResults.RecordNotFound();
                var result = await store.FindAsync(parsed, recordId, cancellationToken);
                return result.Release is null ? ApiResults.NoRelease() : result.Detail is null ? ApiResults.RecordNotFound() : ApiResults.Json(result.Detail);
            }));
}
