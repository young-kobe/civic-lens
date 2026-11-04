using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.Bills;

public static class BillEndpoints
{
    public static void MapBills(this IEndpointRouteBuilder routes)
    {
        var v1 = routes.MapGroup("/v1");
        v1.MapGet("/bills", (HttpContext http, BillStore bills, CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
        {
            var request = BillRequest.Parse(http.Request.Query);
            if (await bills.ListAsync(request, cancellationToken) is not { } page) return ApiResults.NoRelease();
            return request.Cursor is { } cursor && cursor.Release != page.Release ? ApiResults.ReleaseChanged(page.Release) : ApiResults.Json(page);
        }));
        v1.MapGet("/bills/{recordId}", (string recordId, BillStore bills, CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
            PublicationProtocol.IsAnyRecordId(recordId) && await bills.FindAsync(recordId, cancellationToken) is { } bill
                ? ApiResults.Json(bill)
                : ApiResults.RecordNotFound()));
        v1.MapGet("/bills/{recordId}/sponsorships", (string recordId, HttpContext http, BillStore bills, CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
        {
            var request = SponsorshipRequest.Parse(recordId, http.Request.Query);
            if (!PublicationProtocol.IsAnyRecordId(recordId) || await bills.ListSponsorshipsAsync(request, cancellationToken) is not { } page) return ApiResults.RecordNotFound();
            return request.Cursor is { } cursor && cursor.Release != page.Release ? ApiResults.ReleaseChanged(page.Release) : ApiResults.Json(page);
        }));
    }
}
