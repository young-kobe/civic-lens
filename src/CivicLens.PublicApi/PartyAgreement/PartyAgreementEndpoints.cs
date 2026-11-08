using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.PartyAgreement;

public static class PartyAgreementEndpoints
{
    public static void MapPartyAgreement(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/v1/party-agreement", (HttpContext http, PartyAgreementStore store, CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(async () =>
            {
                var request = PartyAgreementRequest.Parse(http.Request.Query);
                return await store.FindAsync(request, cancellationToken) is { } answer
                    ? ApiResults.Json(answer)
                    : ApiResults.NoRelease();
            }));
}
