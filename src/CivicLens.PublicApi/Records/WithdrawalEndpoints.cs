using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.Records;

public static class WithdrawalEndpoints
{
    public static void MapWithdrawals(this IEndpointRouteBuilder routes) =>
        routes.MapGroup("/v1").MapGet("/records/{kind}/{recordId}/withdrawal", (string kind, string recordId, WithdrawalStore store, CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(async () =>
            {
                if (!RecordKinds.TryParse(kind, out var parsed) || !PublicationProtocol.IsAnyRecordId(recordId)) return ApiResults.RecordNotFound();
                return await store.FindAsync(parsed, recordId, cancellationToken) is { } detail
                    ? ApiResults.Json(detail)
                    : ApiResults.RecordNotFound();
            }));
}
