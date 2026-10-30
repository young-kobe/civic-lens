using CivicLens.Application.Documents;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Collection.FeedStatements;

public static class FeedStatementExtractions
{
    private const string Version = "feed-item-statement-v1";
    private const int MaximumLinkLength = 4096;

    public static async Task<FeedStatementReading> ReadAsync(byte[] body, CapturedAttemptResult feed,
        CollectionRequest request, IDocumentTextExtractor extractor, CancellationToken cancellationToken)
    {
        var version = $"{Version}+{extractor.ExtractionVersion}";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var extractions = new List<FeedItemExtraction>();
        var unreadable = 0;
        foreach (var item in NewestFirst(FeedItemParser.Parse(body)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ScopedLink(request, feed, item.Link) is not { } link || seen.Contains(link)) continue;
            if (seen.Count >= request.MaxCandidates) break;
            seen.Add(link);
            var text = await TryExtractAsync(extractor, item.Html, cancellationToken);
            if (text is null) unreadable++;
            if (string.IsNullOrWhiteSpace(text)) continue;
            var rendered = FeedItemRenderer.Render(item with { Link = link }, text);
            extractions.Add(new FeedItemExtraction(link, new DocumentExtraction(feed, version, rendered, part: item.Index)));
        }
        return new FeedStatementReading(extractions, unreadable);
    }

    private static async Task<string?> TryExtractAsync(IDocumentTextExtractor extractor, string html,
        CancellationToken cancellationToken)
    {
        try
        {
            return await extractor.ExtractHtmlFragmentAsync(html, cancellationToken);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static IEnumerable<FeedItem> NewestFirst(IEnumerable<FeedItem> items) =>
        items.OrderByDescending(item => item.Published).ThenBy(item => item.Index);

    private static string? ScopedLink(CollectionRequest request, CapturedAttemptResult feed, string link) =>
        link.Length <= MaximumLinkLength && Uri.TryCreate(new Uri(feed.FinalUrl), link, out var resolved)
            ? request.ResolveCandidate(resolved)?.AbsoluteUri
            : null;
}
