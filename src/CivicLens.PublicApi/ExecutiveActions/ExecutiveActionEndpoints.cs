using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.ExecutiveActions;

public static class ExecutiveActionEndpoints
{
    public static void MapExecutiveActions(this IEndpointRouteBuilder routes) =>
        routes.MapGroup("/v1").MapGet("/executive-actions/{recordId}", (string recordId, ExecutiveActionStore store, CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(async () =>
                PublicationProtocol.IsAnyRecordId(recordId) && await store.FindAsync(recordId, cancellationToken) is { } detail
                    ? ApiResults.Json(detail)
                    : ApiResults.RecordNotFound()));
}
