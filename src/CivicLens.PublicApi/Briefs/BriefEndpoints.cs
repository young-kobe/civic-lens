using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.Briefs;

public static class BriefEndpoints
{
    public static void MapBriefs(this IEndpointRouteBuilder routes) =>
        routes.MapGroup("/v1").MapGet("/briefs/{recordId}", (string recordId, BriefStore briefs, CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(async () =>
                PublicationProtocol.IsAnyRecordId(recordId) && await briefs.FindAsync(recordId, cancellationToken) is { } brief
                    ? ApiResults.Json(brief)
                    : ApiResults.RecordNotFound()));
}
