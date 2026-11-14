using System.Security;
using System.Text;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Pages;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CivicLens.PublicApi.Sitemap;

/// <summary>The gated sitemap and robots file. Each path is the public path of its file under the server-page prefix. The Caddy cutover maps them later.
/// Every address names the public path of its page, never the prefix. The sitemap of one release uses the release ETag rule of the server pages.
/// The robots file allows every crawler and names the sitemap. It reads no view, because it does not change with the release.</summary>
public static class SitemapEndpoints
{
    private const string XmlContentType = "text/xml; charset=utf-8";
    private const string TextContentType = "text/plain; charset=utf-8";
    private const string Namespace = "http://www.sitemaps.org/schemas/sitemap/0.9";

    public static void MapSitemap(this IEndpointRouteBuilder routes, PublicBaseUrl baseUrl)
    {
        routes.MapGet(Path(PublicationProtocol.SitemapPath), (HttpContext context, SitemapReader reader, ServerPageResponder responder, CancellationToken cancellationToken) =>
            ApiResults.GuardAsync(async () => await reader.ReadAsync(null, cancellationToken) is { } data
                ? responder.RespondFile(context, data.Release,
                    () => data.FileCount == 1 ? UrlSet(baseUrl, data.Paths) : Index(baseUrl, data.FileCount), XmlContentType)
                : ApiResults.NoRelease()));
        routes.MapGet(Path("sitemap-{file:int:min(1)}.xml"), (int file, HttpContext context, SitemapReader reader, ServerPageResponder responder,
            CancellationToken cancellationToken) => ApiResults.GuardAsync(async () => await reader.ReadAsync(file, cancellationToken) switch
            {
                null => ApiResults.NoRelease(),
                { Paths.Count: 0 } => await ServerPageEndpoints.NotFoundAsync(context, responder, baseUrl.Join(string.Empty), cancellationToken),
                var data => responder.RespondFile(context, data.Release, () => UrlSet(baseUrl, data.Paths), XmlContentType)
            }));
        routes.MapGet(Path(PublicationProtocol.RobotsPath), (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = PageCache.CacheControl;
            return Results.Content($"User-agent: *\nAllow: /\nSitemap: {baseUrl.Join(PublicationProtocol.SitemapPath)}\n", TextContentType);
        });
    }

    private static string Path(string publicPath) => $"{ServerPageEndpoints.Prefix}/{publicPath}";

    private static string UrlSet(PublicBaseUrl baseUrl, IEnumerable<string> paths) => Document("urlset", "url", paths.Select(baseUrl.Join));

    private static string Index(PublicBaseUrl baseUrl, int fileCount) =>
        Document("sitemapindex", "sitemap", Enumerable.Range(1, fileCount).Select(file => baseUrl.Join(PublicationProtocol.SitemapPagePath(file))));

    private static string Document(string root, string entry, IEnumerable<string> urls)
    {
        var xml = new StringBuilder($"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<{root} xmlns=\"{Namespace}\">\n");
        foreach (var url in urls) xml.Append('<').Append(entry).Append("><loc>").Append(SecurityElement.Escape(url)).Append("</loc></").Append(entry).Append(">\n");
        return xml.Append("</").Append(root).Append(">\n").ToString();
    }
}
