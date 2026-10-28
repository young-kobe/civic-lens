using System.Security;
using System.Text;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Passes every file to the staging directory and remembers the page paths, so the sitemap lists exactly the pages that the release holds.</summary>
internal sealed class ReleaseSite(IReleaseStaging staging, IReleaseRenderer renderer) : IReleaseStaging
{
    private const int MaximumSitemapUrls = 50_000;
    private const int MaximumSitemapBytes = 50 * 1024 * 1024;

    private const string PageExtension = ".html";

    private readonly HashSet<string> pagePaths = new(StringComparer.Ordinal);

    public Task WriteFileAsync(string relativePath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        Remember(relativePath);
        return staging.WriteFileAsync(relativePath, content, cancellationToken);
    }

    public Task LinkFileAsync(string sourceDirectoryName, string relativePath, CancellationToken cancellationToken)
    {
        Remember(relativePath);
        return staging.LinkFileAsync(sourceDirectoryName, relativePath, cancellationToken);
    }

    public Task<string> CompleteAsync(int releaseNumber, CancellationToken cancellationToken) =>
        staging.CompleteAsync(releaseNumber, cancellationToken);

    public ValueTask DisposeAsync() => staging.DisposeAsync();

    public async Task WriteSiteFilesAsync(PublicationRelease manifest, CancellationToken cancellationToken)
    {
        if (renderer.PublicBaseUrl is not { } baseUrl) return;
        await WriteTextAsync(PublicationProtocol.NotFoundPagePath, await renderer.RenderNotFoundAsync(cancellationToken), cancellationToken);
        await WriteTextAsync(PublicationProtocol.SitemapPath, Sitemap(baseUrl, manifest), cancellationToken);
        await WriteTextAsync(PublicationProtocol.RobotsPath, $"User-agent: *\nAllow: /\nSitemap: {baseUrl.Join(PublicationProtocol.SitemapPath)}\n", cancellationToken);
    }

    private void Remember(string relativePath)
    {
        if (relativePath.EndsWith(PageExtension, StringComparison.Ordinal)) pagePaths.Add(relativePath);
    }

    private Task WriteTextAsync(string path, string text, CancellationToken cancellationToken) =>
        WriteFileAsync(path, Encoding.UTF8.GetBytes(text), cancellationToken);

    private string Sitemap(PublicBaseUrl baseUrl, PublicationRelease manifest)
    {
        var excluded = manifest.Withdrawals.Select(withdrawal => PublicationProtocol.RecordPagePath(withdrawal.RecordId))
            .Append(PublicationProtocol.SearchPagePath).Append(PublicationProtocol.ReportPagePath).Append(PublicationProtocol.NotFoundPagePath)
            .ToHashSet(StringComparer.Ordinal);
        var urls = pagePaths.Where(path => !excluded.Contains(path))
            .Select(path => baseUrl.Join(path == PublicationProtocol.HomePath ? string.Empty : path))
            .Order(StringComparer.Ordinal).ToList();
        if (urls.Count > MaximumSitemapUrls)
            throw new ArgumentException($"The sitemap would hold more than {MaximumSitemapUrls} addresses.", nameof(manifest));
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
        foreach (var url in urls) xml.Append("<url><loc>").Append(SecurityElement.Escape(url)).Append("</loc></url>\n");
        xml.Append("</urlset>\n");
        if (Encoding.UTF8.GetByteCount(xml.ToString()) > MaximumSitemapBytes)
            throw new ArgumentException("The sitemap would exceed the sitemap size limit.", nameof(manifest));
        return xml.ToString();
    }
}
