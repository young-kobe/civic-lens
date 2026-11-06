using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.Officials;

public static class OfficialEndpoints
{
    public static void MapOfficials(this IEndpointRouteBuilder routes)
    {
        var v1 = routes.MapGroup("/v1");
        v1.MapGet("/officials", (HttpContext http, OfficialStore officials, CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
        {
            var request = OfficialRequest.Parse(http.Request.Query);
            if (await officials.ListAsync(request, cancellationToken) is not { } page) return ApiResults.NoRelease();
            return request.Cursor is { } cursor && cursor.Release != page.Release ? ApiResults.ReleaseChanged(page.Release) : ApiResults.Json(page);
        }));
        v1.MapGet("/officials/{id}", (string id, OfficialStore officials, CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
            id.Length <= CivicLens.Publication.Contracts.PublishedRecordEntry.MaximumFilterValueLength && await officials.FindAsync(id, cancellationToken) is { } official
                ? ApiResults.Json(official)
                : ApiResults.RecordNotFound()));
    }
}
