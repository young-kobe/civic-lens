using CivicLens.PublicApi.Home;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Pages;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Maps;

/// <summary>Serves the Maps page with the shared cache and renders refused filters in its form.</summary>
public static class MapsPageEndpoint
{
    public static Task<IResult> RespondAsync(HttpContext context, MapsPageReader reader, ServerPageResponder responder, HomePageReader home,
        PublicBaseUrl baseUrl, CancellationToken cancellationToken) => ApiResults.GuardAsync(async () =>
    {
        var query = context.Request.Query;
        MapsPageData? data;
        try
        {
            data = await reader.ReadAsync(query, cancellationToken);
        }
        catch (ArgumentException refused) when (refused.ParamName is not null)
        {
            data = await reader.ReadRefusedAsync(query, refused.ParamName, cancellationToken);
        }

        if (data is null) return ApiResults.NoRelease();
        var statusCode = data.Filter.Invalid is null ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest;
        return await responder.RespondAsync<MapsPage>(context, statusCode, data.Release, new Dictionary<string, object?>
        {
            [nameof(MapsPage.Data)] = data,
            [nameof(MapsPage.RootPath)] = baseUrl.Join(string.Empty),
            [nameof(MapsPage.BrowserApiUrl)] = home.BrowserApiRoot
        }, cancellationToken);
    });
}
