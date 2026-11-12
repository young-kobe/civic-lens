namespace CivicLens.Publication.Contracts;

public sealed record PublishedDocumentVersion
{
    public const int MaximumTextLength = 2_000_000;

    public required string ExtractionId { get; init; }
    public required string Url { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required string Text { get; init; }

    /// <summary>Reads an exact UTF-16 quotation without splitting a character. An invalid or empty span has no quotation.</summary>
    public static string? QuoteAt(string text, int start, int length) =>
        length > 0 && PublishedEvidenceRules.IsRange(text, start, length) ? text.Substring(start, length) : null;

    internal string Host => new Uri(Url).Host;
}
