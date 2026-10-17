namespace CivicLens.Publication.Contracts;

internal static class PublishedEvidenceRules
{
    public static void RequireVersion(PublishedDocumentVersion? version)
    {
        if (version is null || !PublicationProtocol.IsSha256(version.ExtractionId) || version.Text is null ||
            !Uri.TryCreate(version.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            throw new InvalidDataException("Published document version is invalid.");
    }

    public static void RequireSources(PublishedDocumentVersion[]? sources, int maximum, string noun)
    {
        if (sources is null || sources.Length is 0 || sources.Length > maximum)
            throw new InvalidDataException($"Published {noun} requires 1 to {maximum} sources.");
        foreach (var source in sources) RequireVersion(source);
        if (sources.DistinctBy(source => source.ExtractionId).Count() != sources.Length)
            throw new InvalidDataException($"Published {noun} sources must be distinct.");
    }

    public static void RequireCitation(PublishedDocumentVersion[] sources, PublishedCitation? citation, string noun)
    {
        if (citation is null) throw new InvalidDataException("Published citation is missing.");
        var source = sources.FirstOrDefault(item => item.ExtractionId == citation.ExtractionId)
            ?? throw new InvalidDataException($"Published citation must cite one of the {noun}'s sources.");
        RequireQuote(source.Text, citation.Start, citation.Length, citation.Quote);
    }

    public static void RequireEverySourceCited(PublishedDocumentVersion[] sources, IEnumerable<PublishedCitation> citations, string noun)
    {
        var cited = citations.Select(citation => citation.ExtractionId).ToHashSet(StringComparer.Ordinal);
        if (sources.Any(source => !cited.Contains(source.ExtractionId)))
            throw new InvalidDataException($"Each published {noun} source must be cited.");
    }

    public static void RequireQuote(string text, int start, int length, string? quote)
    {
        if (length <= 0 || !IsRange(text, start, length) ||
            SplitsSurrogatePair(text, start) || SplitsSurrogatePair(text, start + length) ||
            !string.Equals(text.Substring(start, length), quote, StringComparison.Ordinal))
            throw new InvalidDataException("Published citation quote does not match its offsets.");
    }

    public static bool IsRange(string text, int start, int length) =>
        start >= 0 && length >= 0 && start <= text.Length - length;

    public static bool AreDistinctAndNotBlank(IEnumerable<string?> labels)
    {
        var all = labels.ToArray();
        return all.All(label => !string.IsNullOrWhiteSpace(label)) &&
            all.Distinct(StringComparer.Ordinal).Count() == all.Length;
    }

    private static bool SplitsSurrogatePair(string text, int index) =>
        index > 0 && index < text.Length && char.IsHighSurrogate(text[index - 1]) && char.IsLowSurrogate(text[index]);
}
