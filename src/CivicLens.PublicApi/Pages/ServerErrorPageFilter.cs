using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;

namespace CivicLens.PublicApi.Pages;

/// <summary>Turns a server failure of a page route into the server error page, so a reader never sees a raw problem document.
/// A problem answer with a 5xx status keeps its status. An exception answers 500 and is logged here, because this filter handles it.
/// The page links home and the list of the section of the failed page. The API routes under /v1 have no such filter and keep their problem answers.</summary>
public sealed class ServerErrorPageFilter(ServerPageResponder responder, PublicBaseUrl baseUrl, ILogger<ServerErrorPageFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var context = invocation.HttpContext;
        int statusCode;
        try
        {
            var result = await next(invocation);
            if (result is not ProblemHttpResult { StatusCode: >= StatusCodes.Status500InternalServerError } problem) return result;
            statusCode = problem.StatusCode;
        }
        catch (Exception exception) when (!context.RequestAborted.IsCancellationRequested)
        {
            logger.LogError(exception, "The server page {Path} failed.", context.Request.Path.Value);
            statusCode = StatusCodes.Status500InternalServerError;
        }
        return await responder.RespondFailureAsync<PublishedServerErrorPage>(context, statusCode, new Dictionary<string, object?>
        {
            [nameof(PublishedServerErrorPage.RootPath)] = baseUrl.Join(string.Empty),
            [nameof(PublishedServerErrorPage.SectionLink)] = PublicSections.SectionOf(context.Request.Path.Value?.TrimStart('/') ?? string.Empty)
        });
    }
}
