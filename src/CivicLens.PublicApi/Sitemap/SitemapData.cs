namespace CivicLens.PublicApi.Sitemap;

/// <summary>The sitemap of the active release. The paths are the public paths of one file, or empty when the request needs only the file count.</summary>
public sealed record SitemapData(int Release, int FileCount, IReadOnlyList<string> Paths);
