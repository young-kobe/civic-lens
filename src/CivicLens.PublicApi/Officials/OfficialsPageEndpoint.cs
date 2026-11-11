using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Pages;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Officials;

/// <summary>The gated officials list page. A page of one release, filter, and cursor never changes, so it uses the shared page cache, as Home does.</summary>
public static class OfficialsPageEndpoint
{
    public static Task<IResult> RespondAsync(HttpContext context, OfficialsPageReader reader, ServerPageResponder responder,
        PublicBaseUrl baseUrl, CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
    {
        var request = OfficialRequest.Parse(context.Request.Query);
        if (await reader.ReadAsync(request, cancellationToken) is not { } data) return ApiResults.NoRelease();
        if (request.Cursor is { } cursor && cursor.Release != data.Release) return Results.Redirect(FirstPage(context.Request));
        return await responder.RespondAsync<OfficialsPage>(context, StatusCodes.Status200OK, data.Release,
            new Dictionary<string, object?>
            {
                [nameof(OfficialsPage.Data)] = data,
                [nameof(OfficialsPage.RootPath)] = baseUrl.Join(string.Empty)
            }, cancellationToken);
    });

    /// <summary>The same filters without the cursor of a release that is no longer active.</summary>
    private static string FirstPage(HttpRequest request) =>
        request.Path + QueryString.Create(request.Query.Where(pair => pair.Key != "cursor")).ToUriComponent();
}
