using CivicLens.PublicWeb.Publication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;
using System.Security.Cryptography;

namespace CivicLens.PublicApi.Pages;

public static class SharedAssetEndpoints
{
    /// <summary>The cache value of the static release for every file. No shared file has a hashed or versioned name yet, so none can be cached for longer.</summary>
    public const string CacheControl = "public, max-age=60";

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    /// <summary>Serves the shared files of <see cref="PublicAssets.Paths"/> at the paths that the static release uses. A request with <c>If-None-Match</c> gets 304 when the file is the same.</summary>
    public static void MapSharedAssets(this IEndpointRouteBuilder routes)
    {
        foreach (var path in PublicAssets.Paths)
        {
            var bytes = EmbeddedPublicAssets.Read(path);
            var tag = new EntityTagHeaderValue($"\"{Convert.ToHexStringLower(SHA256.HashData(bytes))}\"");
            var contentType = ContentTypes.TryGetContentType(path, out var known) ? known : "application/octet-stream";
            routes.MapGet("/" + path, (HttpContext context) =>
            {
                context.Response.Headers.CacheControl = CacheControl;
                return Results.Bytes(bytes, contentType, entityTag: tag);
            });
        }
    }
}
