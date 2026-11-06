using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.Issues;

public static class IssueEndpoints
{
    public static void MapIssues(this IEndpointRouteBuilder routes)
    {
        var v1 = routes.MapGroup("/v1");
        v1.MapGet("/issues/{issueId}", (string issueId, IssueStore issues, CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
        {
            if (issueId.Length > PublishedRecordEntry.MaximumFilterValueLength) return ApiResults.RecordNotFound();
            return await issues.FindAsync(issueId, cancellationToken) is { } counts ? ApiResults.Json(counts) : ApiResults.RecordNotFound();
        }));
    }
}
