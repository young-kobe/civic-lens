using CivicLens.Application.Documents;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Collection.FeedStatements;

public static class FeedStatementExtractions
{
    private const string Version = "feed-item-statement-v1";
    private const int MaximumLinkLength = 4096;

    public static string ExtractionVersion(IDocumentTextExtractor extractor)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        return $"{Version}+{extractor.ExtractionVersion}";
    }

    public static async Task<IReadOnlyList<FeedItemExtraction>> ReadAsync(byte[] body, CapturedAttemptResult feed,
        CollectionRequest request, IDocumentTextExtractor extractor, CancellationToken cancellationToken)
    {
        var version = ExtractionVersion(extractor);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var extractions = new List<FeedItemExtraction>();
        foreach (var item in NewestFirst(FeedItemParser.Parse(body, request.MaxCandidates)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ScopedLink(request, feed, item.Link) is not { } link || !seen.Add(link)) continue;
            var text = await extractor.ExtractHtmlFragmentAsync(item.Html, cancellationToken);
            if (string.IsNullOrWhiteSpace(text)) continue;
            var rendered = FeedItemRenderer.Render(item with { Link = link }, text);
            extractions.Add(new FeedItemExtraction(link, new DocumentExtraction(feed, version, rendered, part: item.Index)));
        }
        return extractions;
    }

    private static IEnumerable<FeedItem> NewestFirst(IEnumerable<FeedItem> items) =>
        items.OrderByDescending(item => item.Published).ThenBy(item => item.Index);

    private static string? ScopedLink(CollectionRequest request, CapturedAttemptResult feed, string link) =>
        link.Length <= MaximumLinkLength && Uri.TryCreate(new Uri(feed.FinalUrl), link, out var resolved)
            ? request.ResolveCandidate(resolved)?.AbsoluteUri
            : null;
}
