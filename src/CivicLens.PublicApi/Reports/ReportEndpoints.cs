using System.Security.Cryptography;
using System.Text;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.Reports;

public static class ReportEndpoints
{
    public const string RateLimitPolicy = "reports";
    private const string BearerPrefix = "Bearer ";

    /// <summary>Maps the report submission. The pull route exists only when a pull token is configured.</summary>
    public static void MapReports(this IEndpointRouteBuilder routes, PublicApiOptions options)
    {
        routes.MapPost(ReaderReportProtocol.SubmitPath, SubmitAsync).RequireRateLimiting(RateLimitPolicy);
        if (options.ReportPullToken is { } token)
            routes.MapGet(ReaderReportProtocol.PullPath, (HttpContext http, ReportStore store, CancellationToken cancellationToken) =>
                ApiResults.GuardAsync(async () => IsAuthorized(http, token)
                    ? ApiResults.Json(await store.PullAsync(
                        ReportCursor.Parse(QueryValues.Value(http.Request.Query, ReaderReportProtocol.AfterParameter)),
                        QueryValues.Number(http.Request.Query, ReaderReportProtocol.LimitParameter, 1, ReaderReportProtocol.MaximumBatchSize)
                            ?? ReaderReportProtocol.DefaultBatchSize,
                        cancellationToken))
                    : Unauthorized(http)));
    }

    private static Task<IResult> SubmitAsync(HttpContext http, [FromServices] TurnstileVerifier? verifier, ReportStore store, CancellationToken cancellationToken) =>
        ApiResults.GuardAsync(async () =>
        {
            if (verifier is null) return Problem(StatusCodes.Status503ServiceUnavailable, "reports are not available");
            var submission = await ReadAsync(http, cancellationToken);
            switch (await verifier.VerifyAsync(submission.TurnstileToken, cancellationToken))
            {
                case TurnstileOutcome.Rejected: return Problem(StatusCodes.Status400BadRequest, "the check failed");
                case TurnstileOutcome.Unavailable: return Problem(StatusCodes.Status503ServiceUnavailable, "the check is not available");
            }
            return await store.StoreAsync(submission, cancellationToken) switch
            {
                ReportReceipt.Stored => Results.Accepted(),
                ReportReceipt.UnknownRecord => Problem(StatusCodes.Status404NotFound, "record not found"),
                ReportReceipt.Closed => Problem(StatusCodes.Status503ServiceUnavailable, "reports are closed for today"),
                _ => ApiResults.NoRelease()
            };
        });

    private static async Task<ReportSubmission> ReadAsync(HttpContext http, CancellationToken cancellationToken)
    {
        if (!http.Request.HasFormContentType) throw new ArgumentException("The body must be form-encoded.");
        http.Features.GetRequiredFeature<IHttpMaxRequestBodySizeFeature>().MaxRequestBodySize = ReaderReportProtocol.MaximumRequestBytes;
        try
        {
            return ReportSubmission.Parse(await http.Request.ReadFormAsync(cancellationToken));
        }
        catch (InvalidDataException exception)
        {
            throw new ArgumentException(exception.Message, exception);
        }
    }

    private static bool IsAuthorized(HttpContext http, string token)
    {
        var header = http.Request.Headers.Authorization.ToString();
        return header.StartsWith(BearerPrefix, StringComparison.Ordinal) &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header[BearerPrefix.Length..]), Encoding.UTF8.GetBytes(token));
    }

    private static IResult Unauthorized(HttpContext http)
    {
        http.Response.Headers.WWWAuthenticate = "Bearer";
        return Problem(StatusCodes.Status401Unauthorized, "a pull token is required");
    }

    private static IResult Problem(int status, string title) => Results.Problem(statusCode: status, title: title);
}
