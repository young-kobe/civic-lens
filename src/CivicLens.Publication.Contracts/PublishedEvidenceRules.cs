namespace CivicLens.Publication.Contracts;

internal static class PublishedEvidenceRules
{
    public static void RequireVersion(PublishedDocumentVersion? version)
    {
        if (version is null || !PublicationProtocol.IsSha256(version.ExtractionId) || version.Text is null ||
            !Uri.TryCreate(version.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            throw new InvalidDataException("Published document version is invalid.");
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
